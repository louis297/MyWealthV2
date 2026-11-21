using Microsoft.Extensions.Logging;
using MyWealthV2.Application.AccountClosures;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Transactions;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.ValueObjects;

namespace MyWealthV2.Application.BankFeed;

/// <summary>
/// Withdrawals R39–R42: the debit line of a withdrawal's payment posts its payout, and the credit line that returns
/// it reverses the payout. The caller runs each inside its own database transaction, one line at a time.
/// </summary>
internal sealed class BankPayouts(IApplicationDbContext db, TimeProvider time, ICurrencyCatalog catalog, ILogger logger)
{
    /// <summary>R40: true when the debit line's end-to-end id names a withdrawal of the line's Tenant that its
    /// payment can be paid for, and the line was handled; false when it names none, so the caller goes on
    /// (bank-returns R2).</summary>
    public async Task<bool> ProcessDebitAsync(BankStatementLine line, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(line.EndToEndId, out var instruction))
        {
            return false;
        }

        var withdrawal = await db.Withdrawals.IgnoreQueryFilters()
            .SingleOrDefaultAsync(row => row.PublicId == instruction && row.TenantId == line.TenantId, cancellationToken);
        var payment = withdrawal is null
            ? null
            : await db.OutgoingPayments.IgnoreQueryFilters()
                .SingleOrDefaultAsync(row => row.WithdrawalId == withdrawal.Id, cancellationToken);
        if (withdrawal is null || payment is null)
        {
            return false;
        }

        // A withdrawal is not paid twice: a second line with its id does not fit it.
        if (payment.Status is OutgoingPaymentStatus.Paid or OutgoingPaymentStatus.Returned)
        {
            await MismatchAsync(line, cancellationToken);
            return true;
        }

        if (payment.Status is not (OutgoingPaymentStatus.Pending or OutgoingPaymentStatus.Accepted)
            || payment.ClientMoneyAccountId != line.ClientMoneyAccountId)
        {
            return false;
        }

        if (line.Amount != withdrawal.Amount)
        {
            await MismatchAsync(line, cancellationToken);
            return true;
        }

        await db.LockAccountAsync(withdrawal.AccountId, cancellationToken);
        var account = await db.Accounts.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(row => row.Id == withdrawal.AccountId, cancellationToken);
        var payee = await db.NominatedBankAccounts.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(row => row.Id == withdrawal.NominatedBankAccountId, cancellationToken);
        var now = time.GetUtcNow();

        // R41: the payout releases its own reservation, so it is the one write the available cash rule exempts (R36).
        // It is posted for a Disabled Customer too: the bank has paid, and the books follow the bank.
        var payout = Transaction.PostPayout(
            withdrawal.TenantId,
            account.Id,
            account.Currency,
            withdrawal.Amount,
            now,
            TransactionSource.Payment(withdrawal.PublicId),
            new BankPayment(line.ValueDate, payment.Reference, payee.AccountName, AccountNumberMask.Mask(payee.AccountNumber)));
        Stamp(payout);
        db.Transactions.Add(payout);
        await db.SaveChangesAsync(cancellationToken);

        withdrawal.MarkPaid(payout.Id);
        payment.MarkPaid();
        line.MarkPayout(account.Id, payout.Id, withdrawal.Id, now);

        // Account closure R17–R19: the payout of a closure's withdrawal completes the closure in this transaction.
        await AccountClosureCompletion.AfterPayoutAsync(db, catalog, logger, withdrawal, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>R42: true when the credit line returns a payout line and has been handled; false when it names no
    /// payout line of its Tenant and client money account (bank-returns R8).</summary>
    public async Task<bool> ProcessReturnAsync(BankStatementLine line, CancellationToken cancellationToken)
    {
        var returned = await BankRecalls.ReturnedLineAsync(db, line, cancellationToken);
        if (returned is null)
        {
            return false;
        }

        if (returned.MatchMethod != StatementLineMatchMethod.Payment || returned.WithdrawalId is not int withdrawalId)
        {
            return false;
        }

        var withdrawal = await db.Withdrawals.IgnoreQueryFilters().SingleAsync(row => row.Id == withdrawalId, cancellationToken);
        if (withdrawal.Status != WithdrawalStatus.Paid || line.Amount != withdrawal.Amount)
        {
            await MismatchAsync(line, cancellationToken);
            return true;
        }

        await db.LockAccountAsync(withdrawal.AccountId, cancellationToken);
        var account = await db.Accounts.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(row => row.Id == withdrawal.AccountId, cancellationToken);
        if (account.Status == AccountStatus.Closed)
        {
            // A Closed account holds nothing; the money is a receipt for a TenantAdmin to place (deposit-matching R26).
            line.MarkUnmatched(UnmatchedReason.AccountClosed);
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }

        if (await AccountClosing.IsClosingAsync(db, account.Id, cancellationToken))
        {
            // Account closure R14: the closing account's payout is fixed; the money waits in suspense.
            line.MarkUnmatched(UnmatchedReason.AccountClosing);
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }

        var payout = await db.Transactions.IgnoreQueryFilters()
            .Include(row => row.CashLegs)
            .Include(row => row.PaymentDetail)
            .SingleAsync(row => row.Id == withdrawal.PayoutTransactionId, cancellationToken);
        var cash = await TransactionCash.SumAsync(db, account.Id, cancellationToken);
        var now = time.GetUtcNow();
        var reversal = payout.Reverse(
            TransactionSource.Payment(withdrawal.PublicId), now, new Dictionary<int, decimal> { [account.Id] = cash });
        Stamp(reversal);
        db.Transactions.Add(reversal);
        await db.SaveChangesAsync(cancellationToken);

        var payment = await db.OutgoingPayments.IgnoreQueryFilters().SingleAsync(row => row.WithdrawalId == withdrawal.Id, cancellationToken);
        withdrawal.MarkReturned(reversal.Id, line.ReturnReason);
        payment.MarkReturned();
        line.MarkPaymentReturn(account.Id, reversal.Id, withdrawal.Id, returned.Id, now);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task MismatchAsync(BankStatementLine line, CancellationToken cancellationToken)
    {
        line.MarkPaymentMismatch();
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>The worker's system name, also when the processing runs outside the worker (deposit-matching R32).</summary>
    private static void Stamp(Transaction transaction)
    {
        transaction.CreatedBy = SystemActor.Name;
        foreach (var leg in transaction.CashLegs)
        {
            leg.CreatedBy = SystemActor.Name;
        }
    }
}
