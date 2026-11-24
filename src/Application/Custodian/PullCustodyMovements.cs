using Microsoft.Extensions.Logging;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.Custodian;

/// <summary>What a pull of one connection did: the safekeeping accounts whose read failed (in-kind-transfers R9, R10).</summary>
public sealed record CustodianPullOutcome(IReadOnlyList<Guid> FailedSafekeepingAccounts)
{
    public bool AnyFailed => FailedSafekeepingAccounts.Count > 0;
}

/// <summary>
/// Reads the feed of every active safekeeping account of one connection through <see cref="ICustodianFeed"/> and
/// stores each new custody movement once, with the new cursor and the page's positions, page by page
/// (docs/features/in-kind-transfers.md R8–R13; ADR 0021 rules 6, 7, 15, 16). Runs as the worker's system user and reads
/// across tenants explicitly.
/// </summary>
public sealed class PullCustodyMovements(
    IApplicationDbContext db,
    ICustodianFeed feed,
    ICustodianConnections connections,
    TimeProvider time,
    ILogger<PullCustodyMovements> logger)
{
    public const int PageLimit = 100;
    public const int MaxPagesPerAccount = 20;

    public async Task<CustodianPullOutcome> RunAsync(string connection, CancellationToken cancellationToken = default)
    {
        if (!connections.IsConfigured(connection))
        {
            logger.LogWarning("Custodian connection {Connection} is not configured on this host; nothing is read.", connection);
            return new CustodianPullOutcome([]);
        }

        var accounts = await db.SafekeepingAccounts.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.IsActive && row.CustodianConnection == connection)
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
                    "Custodian feed read failed: connection {Connection}, safekeeping account {SafekeepingAccountId}, failure {Failure}.",
                    connection, account.PublicId, failure);
            }
        }

        return new CustodianPullOutcome(failed);
    }

    /// <summary>R10: one account on its own; a failure is recorded on it and stores nothing more.</summary>
    private async Task<CustodianFailure?> ReadAccountAsync(string connection, SafekeepingAccount account, CancellationToken cancellationToken)
    {
        for (var page = 0; page < MaxPagesPerAccount; page++)
        {
            db.ClearTrackedChanges();
            var cursor = await db.CustodianFeedCursors.IgnoreQueryFilters()
                .SingleOrDefaultAsync(row => row.SafekeepingAccountId == account.Id, cancellationToken);
            var result = await feed.GetMovementsAsync(
                new CustodianFeedRequest(connection, account.AccountNumber, cursor?.Cursor, PageLimit), cancellationToken);
            var failure = result.Failure ?? Check(result.Page!, account);
            if (failure is not null)
            {
                await RecordFailureAsync(account, cursor, failure.Value, cancellationToken);
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

    /// <summary>R11, R12: a page with any movement or position the platform cannot store is refused whole. A cursor
    /// that does not fit is a response the platform cannot read.</summary>
    private static CustodianFailure? Check(CustodianFeedPage page, SafekeepingAccount account)
    {
        if (page.Cursor is { Length: > CustodianFeedCursor.CursorMaxLength })
        {
            return CustodianFailure.BadResponse;
        }

        foreach (var movement in page.Movements)
        {
            if (movement.Id?.Trim().Length is not (>= 1 and <= CustodyMovement.OutsideIdMaxLength)
                || movement.Direction is null
                || movement.Symbol?.Trim().Length is not (>= 1 and <= CustodyMovement.SymbolMaxLength)
                || !string.Equals(movement.Currency?.Trim(), account.Currency, StringComparison.OrdinalIgnoreCase)
                || movement.Quantity is not > 0m
                || decimal.Round(movement.Quantity.Value, 8) != movement.Quantity.Value
                || movement.SettlementDate is null
                || !FitsReference(movement.InstructionId)
                || !FitsReference(movement.TradeReference)
                || !FitsReference(movement.CorporateActionId))
            {
                return CustodianFailure.InvalidItem;
            }
        }

        if (page.Positions is null)
        {
            return CustodianFailure.InvalidItem;
        }

        var symbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var position in page.Positions)
        {
            if (position.Symbol?.Trim().Length is not (>= 1 and <= CustodianPosition.SymbolMaxLength)
                || !symbols.Add(position.Symbol.Trim())
                || !string.Equals(position.Currency?.Trim(), account.Currency, StringComparison.OrdinalIgnoreCase)
                || position.Quantity is null
                || decimal.Round(position.Quantity.Value, 8) != position.Quantity.Value)
            {
                return CustodianFailure.InvalidItem;
            }
        }

        return null;
    }

    /// <summary>R11: an optional reference is 1–100 characters when present.</summary>
    private static bool FitsReference(string? value) =>
        value is null || value.Trim().Length is >= 1 and <= CustodyMovement.ReferenceIdMaxLength;

    /// <summary>R10, R12, R13: the new movements of one page, the new cursor, the success time and the page's positions,
    /// in one database transaction. A movement already stored for the connection, or earlier in the page, is skipped.</summary>
    private async Task StorePageAsync(
        string connection, SafekeepingAccount account, CustodianFeedCursor? cursor, CustodianFeedPage page,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        await db.ExecuteInTransactionAsync(async token =>
        {
            var ids = page.Movements.Select(movement => movement.Id!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var known = await db.CustodyMovements.IgnoreQueryFilters()
                .Where(row => row.Connection == connection && ids.Contains(row.OutsideId))
                .Select(row => row.OutsideId)
                .ToListAsync(token);

            // R13: the Instrument of the Tenant with the symbol and the safekeeping account's currency, looked up once.
            var symbols = page.Movements.Select(movement => movement.Symbol!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var instruments = (await db.Instruments.IgnoreQueryFilters().AsNoTracking()
                    .Where(row => row.TenantId == account.TenantId && row.QuoteCurrency == account.Currency && symbols.Contains(row.Symbol))
                    .Select(row => new { row.Id, row.Symbol })
                    .ToListAsync(token))
                .GroupBy(row => row.Symbol, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Id, StringComparer.OrdinalIgnoreCase);

            // The database compares outside ids without regard to case, so the page does too.
            var seen = new HashSet<string>(known, StringComparer.OrdinalIgnoreCase);
            foreach (var movement in page.Movements)
            {
                if (!seen.Add(movement.Id!.Trim()))
                {
                    continue;
                }

                var symbol = movement.Symbol!.Trim();
                var stored = CustodyMovement.Receive(
                    account.TenantId, account.Id, connection, movement.Id, movement.Direction!.Value, movement.Kind, symbol,
                    account.Currency, instruments.TryGetValue(symbol, out var instrumentId) ? instrumentId : null,
                    movement.Quantity!.Value, movement.SettlementDate!.Value, movement.InstructionId, movement.TradeReference,
                    movement.CorporateActionId, movement.CounterpartyName, movement.CounterpartyAccount, movement.Reference, now);
                stored.CreatedBy = SystemActor.Name;
                db.CustodyMovements.Add(stored);
            }

            if (cursor is null)
            {
                cursor = CustodianFeedCursor.Start(account.TenantId, account.Id);
                db.CustodianFeedCursors.Add(cursor);
            }

            // R12: the stored positions become the page's, so they always cover exactly the stored movements.
            var old = await db.CustodianPositions.IgnoreQueryFilters()
                .Where(row => row.SafekeepingAccountId == account.Id)
                .ToListAsync(token);
            db.CustodianPositions.RemoveRange(old);
            await db.SaveChangesAsync(token);
            foreach (var position in page.Positions!)
            {
                db.CustodianPositions.Add(CustodianPosition.Create(account.TenantId, account.Id, position.Symbol!.Trim(), position.Quantity!.Value));
            }

            cursor.StorePage(page.Cursor, now);
            await db.SaveChangesAsync(token);
        }, cancellationToken);
    }

    private async Task RecordFailureAsync(
        SafekeepingAccount account, CustodianFeedCursor? cursor, CustodianFailure failure, CancellationToken cancellationToken)
    {
        if (cursor is null)
        {
            cursor = CustodianFeedCursor.Start(account.TenantId, account.Id);
            db.CustodianFeedCursors.Add(cursor);
        }

        cursor.RecordFailure(failure, time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
    }
}
