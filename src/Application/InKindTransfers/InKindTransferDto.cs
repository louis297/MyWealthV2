using MyWealthV2.Application.Common.Models;

namespace MyWealthV2.Application.InKindTransfers;

/// <summary>An in-kind transfer item (docs/features/in-kind-transfers.md R28, §8.1).</summary>
public sealed class InKindTransferDto
{
    public required Guid Id { get; init; }

    public required Guid AccountId { get; init; }

    public required Guid CustomerId { get; init; }

    public required string Direction { get; init; }

    public required InKindTransferInstrumentDto Instrument { get; init; }

    public required decimal Quantity { get; init; }

    public required string Currency { get; init; }

    public decimal? Cost { get; init; }

    public string? AcquiredOn { get; init; }

    public required bool ChangesBeneficialOwner { get; init; }

    public required InKindTransferCounterpartyDto Counterparty { get; init; }

    /// <summary>Null for a Customer (R28).</summary>
    public string? Memo { get; init; }

    public required string Status { get; init; }

    public required PersonRefDto RequestedBy { get; init; }

    public required DateTimeOffset RequestedAt { get; init; }

    public InKindTransferDecisionDto? Decision { get; init; }

    /// <summary>Null for a Customer (R28).</summary>
    public InKindTransferSendDto? Send { get; init; }

    public string? FailureReason { get; init; }

    public InKindTransferSetAsideDto? SetAside { get; init; }

    public Guid? TransactionId { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public required string RowVersion { get; init; }
}

public sealed record InKindTransferInstrumentDto(Guid Id, string Symbol, string Name);

public sealed record InKindTransferCounterpartyDto(string Name, string Account, string HolderName);

public sealed class InKindTransferDecisionDto
{
    public required string Action { get; init; }

    public required PersonRefDto By { get; init; }

    public required DateTimeOffset At { get; init; }

    /// <summary>Null for a Customer (R28).</summary>
    public string? Note { get; init; }
}

public sealed record InKindTransferSendDto(
    int Attempts,
    DateTimeOffset? LastAttemptAt,
    string? LastFailure,
    DateTimeOffset? AcceptedAt,
    string? CustodianTransferId);

public sealed class InKindTransferSetAsideDto
{
    public required PersonRefDto By { get; init; }

    public required DateTimeOffset At { get; init; }

    /// <summary>Null for a Customer (R28).</summary>
    public string? Note { get; init; }
}
