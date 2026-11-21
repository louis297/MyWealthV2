using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.StatementLines;
using MyWealthV2.Application.Transactions;
using MyWealthV2.Domain.Custody;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.ValueObjects;

namespace MyWealthV2.Application.BankFeed;

/// <summary>
/// Bank-returns R3: a debit line whose <c>returnOf</c> names a receipt is a recall or dishonour by the payer's bank.
/// The receipt's deposit is reversed by the flow while the account's available cash allows it; otherwise the line
/// waits in suspense for an adjustment. The caller runs it inside the line's own database transaction.
/// </summary>
internal sealed class BankRecalls(IApplicationDbContext db, TimeProvider time)
{
    /// <summary>The stored line that <paramref name="line"/> takes back, in its Tenant and client money account only
    /// (bank-returns R2); another Tenant's line counts as no line.</summary>
    public static async Task<BankStatementLine?> ReturnedLineAsync(
        IApplicationDbContext db,
        BankStatementLine line,
        CancellationToken cancellationToken)
    {
        var lines = db.BankStatementLines.IgnoreQueryFilters()
            .Where(row => row.TenantId == line.TenantId && row.ClientMoneyAccountId == line.ClientMoneyAccountId);
        return line.RelatedLineId is int relatedId
            ? await lines.SingleOrDefaultAsync(row => row.Id == relatedId, cancellationToken)
            : await lines.SingleOrDefaultAsync(
                row => row.Connection == line.Connection && row.OutsideId == line.ReturnOfOutsideId, cancellationToken);
    }

    public async Task ProcessRecallAsync(BankStatementLine line, CancellationToken cancellationToken)
    {
        var receipt = await ReturnedLineAsync(db, line, cancellationToken);
        if (receipt is null
            || receipt.Direction != BankLineDirection.Credit
            || receipt.ReturnOfOutsideId is not null
            || receipt.Amount != line.Amount)
        {
            await UnknownAsync(line, UnmatchedReason.UnknownReturn, cancellationToken);
            return;
        }

        switch (receipt.Status)
        {
            case BankStatementLineStatus.Received:
                // The receipt is processed first in the same run (oldest first); until then the recall waits.
                return;
            case BankStatementLineStatus.Matched:
                await ReverseAsync(line, receipt, cancellationToken);
                return;
            case BankStatementLineStatus.Unmatched or BankStatementLineStatus.Ignored or BankStatementLineStatus.ReturnRequested:
                await ReturnUncreditedAsync(line, receipt, cancellationToken);
                return;
            default:
                // Already returned, or the bank was asked to send it back.
                await UnknownAsync(line, UnmatchedReason.UnknownReturn, cancellationToken);
                return;
        }
    }

    private async Task ReverseAsync(BankStatementLine line, BankStatementLine receipt, CancellationToken cancellationToken)
    {
        var accountId = receipt.MatchedAccountId!.Value;
        await db.LockAccountAsync(accountId, cancellationToken);
        var account = await db.Accounts.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(row => row.Id == accountId, cancellationToken);
        var cash = await TransactionCash.SumAsync(db, accountId, cancellationToken);
        var reserved = await AccountCash.ReservedAsync(db, accountId, cancellationToken);

        // R3 with withdrawals R36: no balance goes negative and no reserved cash is taken. A Disabled Customer does
        // not stop it: the bank has taken the money back.
        if (account.Status == AccountStatus.Closed
            || cash - line.Amount < 0m
            || !AvailableCash.Allows(cash - line.Amount, reserved))
        {
            await UnknownAsync(line, UnmatchedReason.RecallNotCovered, cancellationToken);
            return;
        }

        var deposit = await db.Transactions.IgnoreQueryFilters()
            .Include(row => row.CashLegs)
            .Include(row => row.BankReceiptDetail)
            .SingleAsync(row => row.Id == receipt.MatchedTransactionId, cancellationToken);
        var now = time.GetUtcNow();
        var reversal = deposit.Reverse(
            TransactionSource.BankFeed(receipt.PublicId), now, new Dictionary<int, decimal> { [accountId] = cash });
        reversal.CreatedBy = SystemActor.Name;
        foreach (var leg in reversal.CashLegs)
        {
            leg.CreatedBy = SystemActor.Name;
        }

        db.Transactions.Add(reversal);
        await db.SaveChangesAsync(cancellationToken);

        receipt.MarkReturned();
        line.MarkRecall(receipt.Id, accountId, reversal.Id, now);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task ReturnUncreditedAsync(BankStatementLine line, BankStatementLine receipt, CancellationToken cancellationToken)
    {
        if (receipt.Status == BankStatementLineStatus.ReturnRequested)
        {
            // R3: a return the bank was not asked for yet is cancelled; one already attempted waits for its answer,
            // which then changes only the payment (R6).
            var payment = await StatementLineReturns.OpenPaymentAsync(db, receipt.Id, cancellationToken);
            if (payment is { Status: OutgoingPaymentStatus.Pending, Attempts: 0 })
            {
                payment.Cancel();
            }
        }

        receipt.MarkReturned();
        line.MarkRecall(receipt.Id, null, null, time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task UnknownAsync(BankStatementLine line, UnmatchedReason reason, CancellationToken cancellationToken)
    {
        line.MarkUnknown(reason);
        await db.SaveChangesAsync(cancellationToken);
    }
}
