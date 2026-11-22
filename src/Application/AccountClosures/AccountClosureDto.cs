using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Withdrawals;

namespace MyWealthV2.Application.AccountClosures;

/// <summary>An account closure item (docs/features/account-closure.md R25, §8.1). A closure has no action, so no
/// <c>rowVersion</c>.</summary>
public sealed class AccountClosureDto
{
    public required Guid Id { get; init; }

    public required Guid AccountId { get; init; }

    public required Guid CustomerId { get; init; }

    public required string Status { get; init; }

    public string? CancelReason { get; init; }

    /// <summary>Null for a Customer (R25).</summary>
    public string? Memo { get; init; }

    public required PersonRefDto RequestedBy { get; init; }

    public required DateTimeOffset RequestedAt { get; init; }

    public required string Currency { get; init; }

    public required decimal CashBalanceAtRequest { get; init; }

    public required decimal PayoutAmount { get; init; }

    /// <summary>The payee with the number masked; null without a payout.</summary>
    public WithdrawalPayeeDto? NominatedBankAccount { get; init; }

    public Guid? WithdrawalId { get; init; }

    /// <summary>The amount written off (positive); null when nothing was.</summary>
    public decimal? WriteOffAmount { get; init; }

    public Guid? WriteOffTransactionId { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public DateTimeOffset? EndedAt { get; init; }
}

/// <summary>R27: the Account's latest closure on the account item.</summary>
public sealed class AccountClosureSummaryDto
{
    public required Guid Id { get; init; }

    public required string Status { get; init; }

    public required decimal PayoutAmount { get; init; }

    public Guid? WithdrawalId { get; init; }

    public required DateTimeOffset RequestedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }
}
