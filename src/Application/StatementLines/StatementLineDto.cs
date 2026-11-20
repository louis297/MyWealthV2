namespace MyWealthV2.Application.StatementLines;

/// <summary>A bank statement line as a TenantAdmin reads it (docs/features/deposit-matching.md §8.1).</summary>
public sealed class StatementLineDto
{
    public required Guid Id { get; init; }

    public required Guid ClientMoneyAccountId { get; init; }

    public required string Currency { get; init; }

    public required string Direction { get; init; }

    public required decimal Amount { get; init; }

    public required string ValueDate { get; init; }

    public string? PayerName { get; init; }

    public string? PayerAccount { get; init; }

    public string? Reference { get; init; }

    public required DateTimeOffset ReceivedAt { get; init; }

    public required string Status { get; init; }

    public string? UnmatchedReason { get; init; }

    public StatementLineMatchDto? Match { get; init; }

    public StatementLineLastActionDto? LastAction { get; init; }

    /// <summary>The PublicId of the line this line returns, else null (withdrawals R44).</summary>
    public Guid? RelatedLineId { get; init; }

    /// <summary>The withdrawal of a payout line and of a returned payout line, else null (withdrawals R44).</summary>
    public Guid? WithdrawalId { get; init; }

    public string? ReturnReason { get; init; }

    public required string RowVersion { get; init; }
}

public sealed class StatementLineMatchDto
{
    public required Guid AccountId { get; init; }

    public required Guid CustomerId { get; init; }

    public required Guid TransactionId { get; init; }

    public required string Method { get; init; }

    public required DateTimeOffset At { get; init; }
}

public sealed class StatementLineLastActionDto
{
    public required string Action { get; init; }

    public string? Note { get; init; }

    public required StatementLinePersonDto By { get; init; }

    public required DateTimeOffset At { get; init; }
}

public sealed record StatementLinePersonDto(Guid Id, string Name);
