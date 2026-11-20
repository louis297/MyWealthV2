using Microsoft.Extensions.Logging;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.ValueObjects;

namespace MyWealthV2.Application.BankFeed;

/// <summary>
/// Processes every <c>Received</c> credit line of one connection, oldest first: posts the deposit of a receipt that
/// quotes one Custodial account's deposit reference, or leaves it unmatched with a reason (deposit-matching R13,
/// R15–R20, R32; ADR 0021 rule 8). Each line is one database transaction, guarded by the line's row version.
/// </summary>
public sealed class ProcessStatementLines(
    IApplicationDbContext db,
    ICurrencyCatalog catalog,
    TimeProvider time,
    ILogger<ProcessStatementLines> logger)
{
    public async Task RunAsync(string connection, CancellationToken cancellationToken = default)
    {
        var ids = await db.BankStatementLines.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.Connection == connection
                          && row.Status == BankStatementLineStatus.Received
                          && row.Direction == BankLineDirection.Credit)
            .OrderBy(row => row.ReceivedAt)
            .ThenBy(row => row.Id)
            .Select(row => row.Id)
            .ToListAsync(cancellationToken);

        foreach (var id in ids)
        {
            db.ClearTrackedChanges();
            try
            {
                await db.ExecuteInTransactionAsync(token => ProcessAsync(id, token), cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Another writer changed the line; the next run sees it again if it is still Received (R13).
                logger.LogWarning("Statement line {LineId} changed while it was processed; it is skipped.", id);
            }
        }
    }

    private async Task ProcessAsync(int id, CancellationToken cancellationToken)
    {
        var line = await db.BankStatementLines.IgnoreQueryFilters()
            .SingleAsync(row => row.Id == id, cancellationToken);
        if (line.Status != BankStatementLineStatus.Received)
        {
            return;
        }

        var (account, reason) = await ChooseAsync(line, cancellationToken);
        if (account is null)
        {
            line.MarkUnmatched(reason!.Value);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var now = time.GetUtcNow();
        var deposit = await BankDeposits.PostAsync(db, line, account, now, SystemActor.Name, cancellationToken);
        line.MarkMatched(account.Id, deposit.Id, now);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>R15–R18: the one Account the line names, or the first reason it is not matched.</summary>
    private async Task<(Account? Account, UnmatchedReason? Reason)> ChooseAsync(
        BankStatementLine line,
        CancellationToken cancellationToken)
    {
        var candidates = DepositReferenceScanner.Candidates(line.ReferenceText);
        if (candidates.Count == 0)
        {
            return (null, UnmatchedReason.NoReference);
        }

        var accounts = await BankDeposits.AccountsAsync(db, line.TenantId, candidates, cancellationToken);
        if (accounts.Count == 0)
        {
            return (null, UnmatchedReason.UnknownReference);
        }

        if (accounts.Count > 1)
        {
            return (null, UnmatchedReason.SeveralReferences);
        }

        await db.LockAccountAsync(accounts[0].Id, cancellationToken);

        // Read again under the lock, so the checks see the row as it is now.
        var account = await db.Accounts.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(row => row.Id == accounts[0].Id, cancellationToken);
        if (!string.Equals(account.Currency.Trim(), line.Currency, StringComparison.OrdinalIgnoreCase))
        {
            return (null, UnmatchedReason.CurrencyMismatch);
        }

        if (account.Status == AccountStatus.Closed)
        {
            return (null, UnmatchedReason.AccountClosed);
        }

        if (BankDeposits.IsDisabled(await BankDeposits.CustomerAsync(db, account, cancellationToken)))
        {
            return (null, UnmatchedReason.CustomerDisabled);
        }

        if (!BankDeposits.AmountFits(catalog, line))
        {
            return (null, UnmatchedReason.AmountPrecision);
        }

        return (account, null);
    }
}
