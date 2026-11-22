using MyWealthV2.Application.Common.Models;

namespace MyWealthV2.Application.Withdrawals;

/// <summary>A withdrawal item (docs/features/withdrawals.md R25, §8.1).</summary>
public sealed class WithdrawalDto
{
    public required Guid Id { get; init; }

    public required Guid AccountId { get; init; }

    public required Guid CustomerId { get; init; }

    public required WithdrawalPayeeDto NominatedBankAccount { get; init; }

    public required decimal Amount { get; init; }

    public required string Currency { get; init; }

    public required string Reference { get; init; }

    /// <summary>Null for a Customer (R25).</summary>
    public string? Memo { get; init; }

    public required string Status { get; init; }

    public required PersonRefDto RequestedBy { get; init; }

    public required DateTimeOffset RequestedAt { get; init; }

    public WithdrawalDecisionDto? Decision { get; init; }

    public WithdrawalPaymentDto? Payment { get; init; }

    public Guid? PayoutTransactionId { get; init; }

    public Guid? ReturnTransactionId { get; init; }

    public string? FailureReason { get; init; }

    public string? ReturnReason { get; init; }

    /// <summary>The PublicId of the account closure that created the withdrawal, else null (account-closure R28).</summary>
    public Guid? AccountClosureId { get; init; }

    public required string RowVersion { get; init; }
}

/// <summary>The nominated bank account a withdrawal pays to, with the number masked (R25).</summary>
public sealed class WithdrawalPayeeDto
{
    public required Guid Id { get; init; }

    public required string BankName { get; init; }

    public required string AccountName { get; init; }

    public required string AccountNumber { get; init; }
}

public sealed class WithdrawalDecisionDto
{
    public required string Action { get; init; }

    public required PersonRefDto By { get; init; }

    public required DateTimeOffset At { get; init; }

    /// <summary>Null for a Customer (R25).</summary>
    public string? Note { get; init; }
}

/// <summary>The outgoing payment of an approved withdrawal (R25).</summary>
public sealed class WithdrawalPaymentDto
{
    public required string Status { get; init; }

    public required int Attempts { get; init; }

    public DateTimeOffset? LastAttemptAt { get; init; }

    public string? LastFailure { get; init; }

    public DateTimeOffset? SentAt { get; init; }
}
