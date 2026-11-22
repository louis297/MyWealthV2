using System.Data;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.ValueObjects;

namespace MyWealthV2.Application.BankReconciliations;

/// <summary>Runs one bank reconciliation of a client money account (bank-reconciliation R6–R15).</summary>
public interface IReconcileClientMoneyAccount
{
    /// <summary>Reads the inputs at SNAPSHOT, computes the run and stores it; answers the run's PublicId.</summary>
    Task<Guid> RunAsync(
        int clientMoneyAccountId,
        ReconciliationTrigger trigger,
        int? ranByUserId,
        CancellationToken cancellationToken);
}

/// <summary>
/// R6: reads local tables only, in one SNAPSHOT transaction across the Tenant of the client money account, and stores
/// the run in its own transaction. The tenant filter is not relied on: every read names the account's Tenant, so the
/// service runs the same on <c>webapi</c> and on <c>worker</c>.
/// </summary>
public sealed class ReconcileClientMoneyAccount(IApplicationDbContext db, TimeProvider time) : IReconcileClientMoneyAccount
{
    private static readonly BankStatementLineStatus[] ItemStatuses =
    [
        BankStatementLineStatus.Received,
        BankStatementLineStatus.Unmatched,
        BankStatementLineStatus.Ignored,
        BankStatementLineStatus.ReturnRequested,
        BankStatementLineStatus.ReturnSent
    ];

    private static readonly WithdrawalStatus[] OpenWithdrawals =
        [WithdrawalStatus.Requested, WithdrawalStatus.Approved, WithdrawalStatus.Sent];

    public async Task<Guid> RunAsync(
        int clientMoneyAccountId,
        ReconciliationTrigger trigger,
        int? ranByUserId,
        CancellationToken cancellationToken)
    {
        db.ClearTrackedChanges();
        ReconciliationInputs? inputs = null;
        var at = time.GetUtcNow();
        await db.ExecuteInTransactionAsync(IsolationLevel.Snapshot, async token =>
        {
            at = time.GetUtcNow();
            inputs = await ReadAsync(clientMoneyAccountId, token);
        }, cancellationToken);

        var run = BankReconciliation.Compute(inputs!, trigger, ranByUserId, at);

        // The daily job runs as the worker's system user, also when the service is called outside the worker.
        run.CreatedBy = SystemActor.Name;
        db.BankReconciliations.Add(run);
        await db.SaveChangesAsync(cancellationToken);
        return run.PublicId;
    }

    private async Task<ReconciliationInputs> ReadAsync(int clientMoneyAccountId, CancellationToken cancellationToken)
    {
        var account = await db.ClientMoneyAccounts.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(row => row.Id == clientMoneyAccountId, cancellationToken);
        var tenantId = account.TenantId;
        var currency = account.Currency;
        var cursor = await db.BankFeedCursors.AsNoTracking()
            .SingleOrDefaultAsync(row => row.ClientMoneyAccountId == account.Id, cancellationToken);

        var accountLines = db.BankStatementLines.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.TenantId == tenantId && row.ClientMoneyAccountId == account.Id);
        var linesTotal = await accountLines
            .SumAsync(row => (decimal?)(row.Direction == BankLineDirection.Credit ? row.Amount : -row.Amount), cancellationToken) ?? 0m;
        var lines = await accountLines
            .Where(row => ItemStatuses.Contains(row.Status))
            .Select(row => new ReconciliationLine(
                row.Id, row.Direction, row.ValueDate, row.Amount, row.Status, row.UnmatchedReason, row.LastActionNote, row.Kind))
            .ToListAsync(cancellationToken);

        // R8: every cash leg of every role on the Tenant's Custodial accounts in the currency, whatever their status.
        var custodialLegs =
            from leg in db.TransactionCashLegs.IgnoreQueryFilters()
            join cashAccount in db.Accounts.IgnoreQueryFilters() on leg.AccountId equals cashAccount.Id
            where cashAccount.TenantId == tenantId
                  && cashAccount.CustodyMode == CustodyMode.Custodial
                  && leg.TenantId == tenantId
                  && leg.Currency == currency
            select leg;
        var custodialCash = await custodialLegs.SumAsync(leg => (decimal?)leg.Amount, cancellationToken) ?? 0m;

        // Interest-run R41: the legs of interest postings are their own two items, by role, not ledger cash.
        var interestLegs = await (
                from leg in custodialLegs
                join transaction in db.Transactions.IgnoreQueryFilters() on leg.TransactionId equals transaction.Id
                where transaction.SourceKind == TransactionSourceKind.InterestRun
                select new { leg.TransactionId, leg.Role, leg.Amount })
            .ToListAsync(cancellationToken);
        var interestTransactions = interestLegs.Select(leg => leg.TransactionId).ToHashSet();

        // R10: the legs split by the client money account their BankFeed or Payment source belongs to, and by type.
        var perTransaction = await (
                from leg in custodialLegs
                where !db.Transactions.IgnoreQueryFilters().Any(transaction =>
                    transaction.Id == leg.TransactionId && transaction.SourceKind == TransactionSourceKind.InterestRun)
                group leg by leg.TransactionId
                into legs
                select new { TransactionId = legs.Key, Sum = legs.Sum(leg => leg.Amount) })
            .ToListAsync(cancellationToken);
        var transactionIds = perTransaction.Select(row => row.TransactionId).ToList();
        var transactions = await (
                from transaction in db.Transactions.IgnoreQueryFilters()
                where transaction.TenantId == tenantId && transactionIds.Contains(transaction.Id)
                select new
                {
                    transaction.Id,
                    transaction.Type,
                    OriginalType = db.Transactions.IgnoreQueryFilters()
                        .Where(original => original.Id == transaction.OriginalTransactionId)
                        .Select(original => (TransactionType?)original.Type)
                        .FirstOrDefault(),
                    Through = transaction.SourceKind == TransactionSourceKind.BankFeed
                        ? db.BankStatementLines.IgnoreQueryFilters()
                            .Where(line => line.TenantId == tenantId && line.PublicId == transaction.SourcePublicId)
                            .Select(line => (int?)line.ClientMoneyAccountId)
                            .FirstOrDefault()
                        : transaction.SourceKind == TransactionSourceKind.Payment
                            ? (from withdrawal in db.Withdrawals.IgnoreQueryFilters()
                               join payment in db.OutgoingPayments.IgnoreQueryFilters() on withdrawal.Id equals payment.WithdrawalId
                               where withdrawal.TenantId == tenantId
                                     && withdrawal.PublicId == transaction.SourcePublicId
                                     && payment.Kind == OutgoingPaymentKind.Withdrawal
                               select (int?)payment.ClientMoneyAccountId).FirstOrDefault()
                            : null
                })
            .ToDictionaryAsync(row => row.Id, cancellationToken);
        var ledger = perTransaction
            .Select(row => (Sum: row.Sum, Info: transactions[row.TransactionId]))
            .GroupBy(row => (row.Info.Type, row.Info.OriginalType, row.Info.Through))
            .Select(group => new ReconciliationLedgerCash(
                group.Key.Type, group.Key.OriginalType, group.Key.Through, group.Sum(row => row.Sum), group.Count()))
            .ToList();

        // R14: information, not items.
        var reserved = await (
                from withdrawal in db.Withdrawals.IgnoreQueryFilters()
                join cashAccount in db.Accounts.IgnoreQueryFilters() on withdrawal.AccountId equals cashAccount.Id
                where withdrawal.TenantId == tenantId
                      && cashAccount.CustodyMode == CustodyMode.Custodial
                      && withdrawal.Currency == currency
                      && OpenWithdrawals.Contains(withdrawal.Status)
                select (decimal?)withdrawal.Amount)
            .SumAsync(cancellationToken) ?? 0m;
        var inFlight = await db.OutgoingPayments.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.TenantId == tenantId
                          && row.ClientMoneyAccountId == account.Id
                          && row.Kind == OutgoingPaymentKind.Withdrawal
                          && (row.Status == OutgoingPaymentStatus.Accepted
                              || (row.Status == OutgoingPaymentStatus.Pending && row.Attempts > 0)))
            .GroupBy(_ => 1)
            .Select(group => new { Count = group.Count(), Amount = group.Sum(row => row.Amount) })
            .SingleOrDefaultAsync(cancellationToken);

        return new ReconciliationInputs
        {
            TenantId = tenantId,
            ClientMoneyAccountId = account.Id,
            Currency = currency,
            StatementBalance = cursor?.Balance,
            BalanceAt = cursor?.BalanceAt,
            OpeningBalance = cursor?.OpeningBalance,
            StatementLinesTotal = linesTotal,
            Lines = lines,
            CustodialCash = custodialCash,
            Ledger = ledger,
            InterestCredited = interestLegs.Where(leg => leg.Role == CashLegRole.Principal).Sum(leg => leg.Amount),
            InterestCreditedCount = interestTransactions.Count,
            TaxWithheld = interestLegs.Where(leg => leg.Role == CashLegRole.Tax).Sum(leg => leg.Amount),
            TaxWithheldCount = interestLegs.Where(leg => leg.Role == CashLegRole.Tax).Select(leg => leg.TransactionId).Distinct().Count(),
            ReservedCash = reserved,
            PaymentsInFlightCount = inFlight?.Count ?? 0,
            PaymentsInFlightAmount = inFlight?.Amount ?? 0m,
            FeedLastSucceededAt = cursor?.LastSucceededAt,
            FeedLastFailedAt = cursor?.LastFailedAt,
            FeedLastFailure = cursor?.LastFailure
        };
    }
}
