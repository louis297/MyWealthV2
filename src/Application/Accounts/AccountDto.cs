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

    public required string Status { get; init; }

    public required bool IsActive { get; init; }

    public required string RowVersion { get; init; }

    /// <summary>The Account's latest account closure, or null when it has none (account-closure R27).</summary>
    public AccountClosureSummaryDto? Closure { get; init; }

    public static AccountDto From(
        Account account,
        Tenant tenant,
        User customer,
        decimal cashBalance,
        decimal reservedCash,
        AccountClosureSummaryDto? closure = null) => new()
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
        Status = account.Status.ToString(),
        IsActive = account.IsActive,
        RowVersion = Convert.ToBase64String(account.RowVersion ?? []),
        Closure = closure
    };
}
