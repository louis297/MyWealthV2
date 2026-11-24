namespace MyWealthV2.Application.StatementLines;

/// <summary>A bank statement line as a TenantAdmin reads it (docs/features/deposit-matching.md §8.1).</summary>
public sealed class StatementLineDto
{
    public required Guid Id { get; init; }

    public required Guid ClientMoneyAccountId { get; init; }

    public required string Currency { get; init; }

    public required string Direction { get; init; }

    /// <summary><c>Transfer</c> or <c>Interest</c> (interest-run R39).</summary>
    public string Kind { get; init; } = "Transfer";

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

    /// <summary>Trade-settlement R21: the trade settlement whose cash this line is, else null.</summary>
    public Guid? TradeSettlementId { get; init; }

    /// <summary>Migration-batches R30: the batch the line funds, else null.</summary>
    public Guid? MigrationBatchId { get; init; }

    /// <summary>Dividends R53: the dividend the line is the receipt of, else null.</summary>
    public Guid? CorporateActionId { get; init; }

    /// <summary>The newest return payment of a receipt marked for return, else null (bank-returns R11).</summary>
    public StatementLineReturnPaymentDto? ReturnPayment { get; init; }

    public required string RowVersion { get; init; }
}

/// <summary>A match. The account, customer and transaction are null on a match with no Transaction: the debit of a
/// receipt return, or the recall of a receipt never credited (bank-returns R10, R11).</summary>
public sealed class StatementLineMatchDto
{
    public Guid? AccountId { get; init; }

    public Guid? CustomerId { get; init; }

    public Guid? TransactionId { get; init; }

    public required string Method { get; init; }

    public required DateTimeOffset At { get; init; }
}

public sealed class StatementLineLastActionDto
{
    public required string Action { get; init; }

    public string? Note { get; init; }

    /// <summary>The person, or null when the platform acted (interest-run R40).</summary>
    public StatementLinePersonDto? By { get; init; }

    public required DateTimeOffset At { get; init; }
}

public sealed record StatementLinePersonDto(Guid Id, string Name);

/// <summary>The return payment of a receipt (bank-returns R11).</summary>
public sealed class StatementLineReturnPaymentDto
{
    public required string Status { get; init; }

    public required int Attempts { get; init; }

    public string? LastFailure { get; init; }
}
