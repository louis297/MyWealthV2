using Microsoft.Extensions.Logging;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.ValueObjects;

namespace MyWealthV2.Application.BankFeed;

/// <summary>What a pull of one connection did: the client money accounts whose read failed (deposit-matching R7).</summary>
public sealed record BankPullOutcome(IReadOnlyList<Guid> FailedClientMoneyAccounts)
{
    public bool AnyFailed => FailedClientMoneyAccounts.Count > 0;
}

/// <summary>
/// Reads the statement of every active client money account of one connection through <see cref="IBankFeed"/> and
/// stores each new line once, with the new cursor, page by page (deposit-matching R5, R7–R12, R14; ADR 0021 rules 6,
/// 7, 15, 16). Runs as the worker's system user and reads across tenants explicitly.
/// </summary>
public sealed class PullBankStatements(
    IApplicationDbContext db,
    IBankFeed feed,
    IBankConnections connections,
    TimeProvider time,
    ILogger<PullBankStatements> logger)
{
    public const int PageLimit = 100;
    public const int MaxPagesPerAccount = 20;

    public async Task<BankPullOutcome> RunAsync(string connection, CancellationToken cancellationToken = default)
    {
        if (!connections.IsConfigured(connection))
        {
            logger.LogWarning("Bank connection {Connection} is not configured on this host; nothing is read.", connection);
            return new BankPullOutcome([]);
        }

        var accounts = await db.ClientMoneyAccounts.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.IsActive && row.BankConnection == connection)
            .OrderBy(row => row.Id)
            .ToListAsync(cancellationToken);

        var failed = new List<Guid>();
        foreach (var account in accounts)
        {
            var failure = await ReadAccountAsync(connection, account, cancellationToken);
            if (failure is not null)
            {
                failed.Add(account.PublicId);
                logger.LogError(
                    "Bank statement read failed: connection {Connection}, client money account {ClientMoneyAccountId}, failure {Failure}.",
                    connection, account.PublicId, failure);
            }
        }

        return new BankPullOutcome(failed);
    }

    /// <summary>Reads one account on its own (R7): a failure is recorded on it and stores nothing more.</summary>
    private async Task<BankFeedFailure?> ReadAccountAsync(
        string connection,
        ClientMoneyAccount account,
        CancellationToken cancellationToken)
    {
        for (var page = 0; page < MaxPagesPerAccount; page++)
        {
            db.ClearTrackedChanges();
            var cursor = await db.BankFeedCursors
                .SingleOrDefaultAsync(row => row.ClientMoneyAccountId == account.Id, cancellationToken);
            var result = await feed.GetStatementLinesAsync(
                new BankFeedRequest(connection, account.AccountNumber, cursor?.Cursor, PageLimit), cancellationToken);
            var failure = result.Failure ?? Check(result.Page!, account);
            if (failure is not null)
            {
                await RecordFailureAsync(account.Id, cursor, failure.Value, cancellationToken);
                return failure;
            }

            await StorePageAsync(connection, account, cursor, result.Page!, cancellationToken);
            if (!result.Page!.HasMore)
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>R10: a page with any line the platform cannot store is refused whole. A cursor that does not fit is a
    /// response the platform cannot read.</summary>
    private static BankFeedFailure? Check(BankFeedPage page, ClientMoneyAccount account)
    {
        if (page.Cursor is { Length: > BankFeedCursor.CursorMaxLength })
        {
            return BankFeedFailure.BadResponse;
        }

        foreach (var line in page.Lines)
        {
            if (line.ValueDate is null
                || line.Direction is null
                || line.Amount is null
                || !BankStatementLine.IsStorable(line.Id, line.Direction.Value, line.Amount.Value)
                || !string.Equals(line.Currency?.Trim(), account.Currency, StringComparison.OrdinalIgnoreCase))
            {
                return BankFeedFailure.InvalidLine;
            }
        }

        return null;
    }

    /// <summary>R8: the new lines of one page, the new cursor and the success time, in one database transaction. A
    /// line already stored for the connection, or earlier in the page, is skipped.</summary>
    private async Task StorePageAsync(
        string connection,
        ClientMoneyAccount account,
        BankFeedCursor? cursor,
        BankFeedPage page,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        await db.ExecuteInTransactionAsync(async token =>
        {
            var ids = page.Lines.Select(line => line.Id!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var known = await db.BankStatementLines.IgnoreQueryFilters()
                .Where(row => row.Connection == connection && ids.Contains(row.OutsideId))
                .Select(row => row.OutsideId)
                .ToListAsync(token);

            // The database compares outside ids without regard to case, so the page does too.
            var seen = new HashSet<string>(known, StringComparer.OrdinalIgnoreCase);
            foreach (var line in page.Lines)
            {
                if (!seen.Add(line.Id!))
                {
                    continue;
                }

                var stored = BankStatementLine.Receive(
                    account.TenantId,
                    account.Id,
                    connection,
                    line.Id!,
                    line.ValueDate!.Value,
                    line.Direction!.Value,
                    line.Amount!.Value,
                    account.Currency,
                    line.PayerName,
                    PayerAccountMask.Mask(line.PayerAccount),
                    line.Reference,
                    now);

                // The worker's system user, also when the pull runs outside the worker (deposit-matching R32).
                stored.CreatedBy = SystemActor.Name;
                db.BankStatementLines.Add(stored);
            }

            if (cursor is null)
            {
                cursor = BankFeedCursor.Start(account.Id);
                db.BankFeedCursors.Add(cursor);
            }

            cursor.Advance(page.Cursor, now);
            await db.SaveChangesAsync(token);
        }, cancellationToken);
    }

    private async Task RecordFailureAsync(
        int clientMoneyAccountId,
        BankFeedCursor? cursor,
        BankFeedFailure failure,
        CancellationToken cancellationToken)
    {
        if (cursor is null)
        {
            cursor = BankFeedCursor.Start(clientMoneyAccountId);
            db.BankFeedCursors.Add(cursor);
        }

        cursor.Fail(failure, time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
    }
}
