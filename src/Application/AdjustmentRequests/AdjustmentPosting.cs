using FluentValidation.Results;
using MyWealthV2.Application.AccountClosures;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Transactions;
using MyWealthV2.Domain.Custody;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using MyWealthV2.Domain.ValueObjects;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.AdjustmentRequests;

/// <summary>
/// The one rule of what an adjustment may do (adjustment-review §5): the checks of adjustments R5–R15, in the order
/// of adjustments §4.2, on the state under the Account lock. The request runs them without storing a Transaction
/// (R4); the approval runs them again and posts (R9), so the two cannot disagree.
/// </summary>
internal static class AdjustmentPosting
{
    public const string AccountIdKey = "AccountId";
    public const string SecurityKey = "Security";

    public static void EnsureTenantActive(Tenant tenant)
    {
        if (!tenant.IsActive)
        {
            throw new TargetDisabledException(
                "tenant",
                tenant.PublicId,
                "Tenant is disabled",
                "Cannot create a transaction while the tenant is disabled. Enable the tenant first.");
        }
    }

    /// <summary>Adjustments R15, R5, account closure R12: the Account and its Customer accept an adjustment. Runs after
    /// the Account lock.</summary>
    public static async Task EnsureAccountAcceptsAsync(
        IApplicationDbContext db, Account account, User customer, CancellationToken cancellationToken)
    {
        if (account.Status == AccountStatus.Closed)
        {
            throw new ValidationException([
                new ValidationFailure(string.Empty, "Cannot post a transaction while the account is closed. Reopen the account first.")
            ]);
        }

        await AccountClosing.EnsureNotClosingAsync(db, account.Id, cancellationToken);

        if (customer.Status == UserStatus.Disabled)
        {
            throw new TargetDisabledException(
                "user",
                customer.PublicId,
                "User is disabled",
                "Cannot create a transaction while the customer is disabled. Enable the customer first.");
        }

        var refusal = CustodyRules.RefusalMessage(account.CustodyMode, TransactionType.Adjustment);
        if (refusal is not null)
        {
            throw new ValidationException([new ValidationFailure(AccountIdKey, refusal)]);
        }
    }

    /// <summary>Adjustments R10, R12: the holding before the adjustment; a disabled instrument only corrects a position
    /// the Account already holds.</summary>
    public static async Task<AdjustmentHoldingChange> HoldingChangeAsync(
        IApplicationDbContext db,
        Instrument instrument,
        int accountId,
        decimal quantityChange,
        decimal costChange,
        CancellationToken cancellationToken)
    {
        var holding = await db.Holdings.SingleOrDefaultAsync(
            row => row.AccountId == accountId && row.InstrumentId == instrument.Id,
            cancellationToken);
        if (!instrument.IsActive && holding is null)
        {
            throw new ValidationException([new ValidationFailure(SecurityKey, "Instrument is disabled.")]);
        }

        return new AdjustmentHoldingChange(
            instrument.Id,
            instrument.QuoteCurrency.Trim(),
            quantityChange,
            costChange,
            holding?.Quantity ?? 0m,
            holding?.CostAmount ?? 0m);
    }

    /// <summary>
    /// Orders R40, adjustments R9, R11, R13: the reserved quantity, the cash places, and the <c>Adjustment</c> built by
    /// <see cref="Transaction.PostAdjustment"/> with its own checks. The Transaction is not added to the context.
    /// </summary>
    public static async Task<Transaction> BuildAsync(
        IApplicationDbContext db,
        ICurrencyCatalog catalog,
        int tenantId,
        Account account,
        AdjustmentReason reason,
        string memo,
        string? reference,
        decimal? amount,
        AdjustmentHoldingChange? holdingChange,
        DateTimeOffset bookedAt,
        TransactionSource source,
        CancellationToken cancellationToken)
    {
        if (holdingChange is { QuantityChange: < 0m })
        {
            var reserved = await Orders.OrderReservations.ReservedQuantityAsync(
                db, account.Id, holdingChange.InstrumentId, cancellationToken);
            if (reserved > 0m
                && !AvailableQuantity.Allows(holdingChange.CurrentQuantity + holdingChange.QuantityChange, reserved))
            {
                var symbol = await db.Instruments.AsNoTracking()
                    .Where(row => row.Id == holdingChange.InstrumentId)
                    .Select(row => row.Symbol)
                    .SingleAsync(cancellationToken);
                throw new ValidationException([
                    new ValidationFailure(
                        SecurityKey,
                        $"{Orders.OrderChecks.Units(reserved)} {symbol} is reserved for sell orders and transfers out.")
                ]);
            }
        }

        if (amount is { } cash)
        {
            CashAmountPlaces.EnsureFits(catalog, Money.Create(0m, account.Currency).Currency, cash);
        }

        var currentSum = await TransactionCash.SumAsync(db, account.Id, cancellationToken);
        try
        {
            return Transaction.PostAdjustment(
                tenantId,
                account.Id,
                account.Type,
                account.Currency,
                reason,
                memo,
                reference,
                bookedAt,
                source,
                amount,
                currentSum,
                holdingChange);
        }
        catch (DomainException exception)
        {
            var field = exception.Message == Transaction.AdjustmentHoldingMessage ? SecurityKey : string.Empty;
            throw new ValidationException([new ValidationFailure(field, exception.Message)]);
        }
    }

    /// <summary>Posting R9, withdrawals R36, orders R39: the cash <c>SUM</c> after the adjustment is not negative and
    /// covers the cash reserved for withdrawals and orders.</summary>
    public static async Task EnsureCashAfterAsync(
        IApplicationDbContext db, ICurrencyCatalog catalog, Account account, decimal cashSumAfter, CancellationToken cancellationToken)
    {
        if (cashSumAfter < 0)
        {
            throw new ValidationException([new ValidationFailure(string.Empty, "Cash balance cannot become negative.")]);
        }

        await AvailableCashGuard.EnsureAsync(db, catalog, account, cashSumAfter, cancellationToken);
    }
}
