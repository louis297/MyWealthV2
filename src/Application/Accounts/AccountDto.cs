using MyWealthV2.Application.AccountClosures;
using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Application.Accounts;

public sealed class AccountDto
{
    public required Guid Id { get; init; }

    public required Guid TenantId { get; init; }

    public required Guid CustomerId { get; init; }

    public required string Name { get; init; }

    public required string Type { get; init; }

    public required string CustodyMode { get; init; }

    public required string Currency { get; init; }

    /// <summary>The deposit reference of a Custodial account; null on an External one (client-money-accounts R14).</summary>
    public required string? DepositReference { get; init; }

    public required decimal CashBalance { get; init; }

    /// <summary>The sum of the Account's open withdrawals; 0 on an External account (withdrawals R35).</summary>
    public required decimal ReservedCash { get; init; }

    /// <summary>Cash balance minus reserved cash (withdrawals R35).</summary>
    public required decimal AvailableCash { get; init; }

    /// <summary>The net cash of the Account's posted sell fills that have not settled; 0 on an External account
    /// (orders R44).</summary>
    public required decimal UnsettledCash { get; init; }

    /// <summary>Available cash minus unsettled cash, never below 0; the cash balance on an External account
    /// (orders R44).</summary>
    public required decimal WithdrawableCash { get; init; }

    public required string Status { get; init; }

    public required bool IsActive { get; init; }

    public required string RowVersion { get; init; }

    /// <summary>The Account's latest account closure, or null when it has none (account-closure R27).</summary>
    public AccountClosureSummaryDto? Closure { get; init; }

    /// <summary>The sum of the account's daily accrual rows not yet posted, 8 decimals; 0 on an External account (interest-run R35).</summary>
    public decimal AccruedInterest { get; init; }

    /// <summary>The last UTC day the account was accrued through, or null (interest-run R35).</summary>
    public DateOnly? InterestAccruedThrough { get; init; }

    /// <summary>Valuation-run R20: the last day a stored valuation exists for; null before the first and on an External
    /// account.</summary>
    public DateOnly? ValuedThrough { get; init; }

    /// <summary>Dividends R17, R52: the standing reinvestment election; <c>false</c> on an External account.</summary>
    public bool ReinvestDividends { get; init; }

    /// <summary>Dividends R52: the number of <c>Pending</c> dividend entitlements of the account.</summary>
    public int PendingDividends { get; init; }

    public static AccountDto From(
        Account account,
        Tenant tenant,
        User customer,
        decimal cashBalance,
        decimal reservedCash,
        AccountClosureSummaryDto? closure = null,
        decimal accruedInterest = 0m,
        decimal unsettledCash = 0m,
        int pendingDividends = 0) => new()
    {
        Id = account.PublicId,
        TenantId = tenant.PublicId,
        CustomerId = customer.PublicId,
        Name = account.Name,
        Type = account.Type.ToString(),
        CustodyMode = account.CustodyMode.ToString(),
        Currency = account.Currency,
        DepositReference = account.DepositReference?.Value,
        CashBalance = cashBalance,
        ReservedCash = reservedCash,
        AvailableCash = Domain.Custody.AvailableCash.Of(cashBalance, reservedCash),
        UnsettledCash = unsettledCash,
        WithdrawableCash = account.CustodyMode == Domain.Enums.CustodyMode.Custodial
            ? Domain.Custody.AvailableCash.Withdrawable(cashBalance, reservedCash, unsettledCash)
            : cashBalance,
        Status = account.Status.ToString(),
        IsActive = account.IsActive,
        RowVersion = Convert.ToBase64String(account.RowVersion ?? []),
        Closure = closure,
        AccruedInterest = accruedInterest,
        InterestAccruedThrough = account.InterestAccruedThrough,
        ValuedThrough = account.ValuedThrough,
        ReinvestDividends = account.ReinvestDividends,
        PendingDividends = pendingDividends
    };
}
