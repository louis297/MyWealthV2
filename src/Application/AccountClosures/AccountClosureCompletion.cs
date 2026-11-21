using Microsoft.Extensions.Logging;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Transactions;
using MyWealthV2.Domain.Custody;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.ValueObjects;

namespace MyWealthV2.Application.AccountClosures;

/// <summary>
/// Account closure R17–R19: after the payout of a closure's withdrawal is posted, in the same database transaction
/// and with the Account still locked, the remainder is written off, the Account closed and the closure completed;
/// or, if the Account cannot be closed, the closure ends <c>NotClosable</c>. The caller saves.
/// </summary>
internal static class AccountClosureCompletion
{
    public static async Task AfterPayoutAsync(
        IApplicationDbContext db,
        ICurrencyCatalog catalog,
        ILogger logger,
        Withdrawal withdrawal,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var closure = await db.AccountClosures.IgnoreQueryFilters()
            .SingleOrDefaultAsync(row => row.WithdrawalId == withdrawal.Id && row.Status == AccountClosureStatus.InProgress,
                cancellationToken);
        if (closure is null)
        {
            return;
        }

        var account = await db.Accounts.IgnoreQueryFilters().SingleAsync(row => row.Id == withdrawal.AccountId, cancellationToken);
        var currency = account.Currency.Trim();
        var places = catalog.TryGet(currency)!.DecimalPlaces;
        var cash = await TransactionCash.SumAsync(db, account.Id, cancellationToken);
        var holds = await db.Holdings.IgnoreQueryFilters().AnyAsync(row => row.AccountId == account.Id, cancellationToken);

        // R19: against R16 the Account still has cash above a remainder or a holding. The payout stays posted (the
        // bank has paid); nothing is written off and the Account stays Open.
        if (!ClosureAmounts.IsRemainder(cash, places) || holds)
        {
            closure.Cancel(AccountClosureCancelReason.NotClosable, now);
            logger.LogError(
                "Account closure {ClosureId} could not close account {AccountId}: cash {Cash} {Currency}, holdings {Holds} after the payout.",
                closure.PublicId, account.PublicId, cash, currency, holds);
            return;
        }

        Transaction? writeOff = null;
        if (cash > 0m)
        {
            // R18: CreatedBy is the worker's system name when the write-off is posted with the payout.
            writeOff = Transaction.PostRemainderWriteOff(
                account.TenantId, account.Id, currency, places, cash, now, TransactionSource.AccountClosure(closure.PublicId));
            writeOff.CreatedBy = SystemActor.Name;
            writeOff.CashLegs.ForEach(leg => leg.CreatedBy = SystemActor.Name);
            db.Transactions.Add(writeOff);
            await db.SaveChangesAsync(cancellationToken);
        }

        account.Close();
        closure.Complete(writeOff is null ? null : cash, writeOff?.Id, now);
    }
}
