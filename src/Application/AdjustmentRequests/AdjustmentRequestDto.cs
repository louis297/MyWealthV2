using MyWealthV2.Application.Common.Models;

namespace MyWealthV2.Application.AdjustmentRequests;

/// <summary>An adjustment request item (docs/features/adjustment-review.md R19, §8.1).</summary>
public sealed class AdjustmentRequestDto
{
    public required Guid Id { get; init; }

    public required Guid AccountId { get; init; }

    public required Guid CustomerId { get; init; }

    public required string Reason { get; init; }

    public required string Memo { get; init; }

    public string? Reference { get; init; }

    /// <summary>The cash part, or null when the request has none.</summary>
    public decimal? Amount { get; init; }

    /// <summary>The Account currency.</summary>
    public required string Currency { get; init; }

    public AdjustmentRequestSecurityDto? Security { get; init; }

    public required string Status { get; init; }

    public required PersonRefDto RequestedBy { get; init; }

    public required DateTimeOffset RequestedAt { get; init; }

    public AdjustmentRequestDecisionDto? Decision { get; init; }

    /// <summary>The posted <c>Adjustment</c> of an <c>Approved</c> request, else null.</summary>
    public Guid? TransactionId { get; init; }

    public required string RowVersion { get; init; }
}

public sealed record AdjustmentRequestInstrumentDto(Guid Id, string Symbol, string Name);

public sealed class AdjustmentRequestSecurityDto
{
    public required AdjustmentRequestInstrumentDto Instrument { get; init; }

    public required decimal QuantityChange { get; init; }

    public required decimal CostChange { get; init; }

    public required string CostCurrency { get; init; }
}

public sealed class AdjustmentRequestDecisionDto
{
    public required string Action { get; init; }

    public required PersonRefDto By { get; init; }

    public required DateTimeOffset At { get; init; }

    public string? Note { get; init; }
}
