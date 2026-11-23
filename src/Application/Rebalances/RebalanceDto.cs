using System.Text.Json.Serialization;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Orders;

namespace MyWealthV2.Application.Rebalances;

/// <summary>A rebalance item (docs/features/rebalances.md §8.1). The list leaves <see cref="Targets"/>,
/// <see cref="Lines"/> and <see cref="Orders"/> out (R27).</summary>
public sealed class RebalanceDto
{
    public required Guid Id { get; init; }

    public required Guid AccountId { get; init; }

    public required Guid CustomerId { get; init; }

    public required RebalancePortfolioDto Portfolio { get; init; }

    public required string Trigger { get; init; }

    public required string Status { get; init; }

    public required string BaseCurrency { get; init; }

    public required decimal? TotalValue { get; init; }

    public required decimal? Cash { get; init; }

    public required decimal MinimumTrade { get; init; }

    public required decimal CashResidualPercent { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<RebalanceTargetDto>? Targets { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<RebalanceLineDto>? Lines { get; init; }

    /// <summary>The orders it placed, oldest first, with their wave.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<RebalanceOrderDto>? Orders { get; init; }

    public required int Waves { get; init; }

    /// <summary>On a <c>Failed</c> or <c>Incomplete</c> rebalance; else null.</summary>
    public required RebalanceFailureDto? Failure { get; init; }

    /// <summary>Null for the job and a follow-up.</summary>
    public required PersonRefDto? ProposedBy { get; init; }

    public required DateTimeOffset ProposedAt { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }

    public required PersonRefDto? ConfirmedBy { get; init; }

    public required DateTimeOffset? ConfirmedAt { get; init; }

    public required RebalanceCancelRequestDto? CancelRequest { get; init; }

    public required string? CancelReason { get; init; }

    public required bool FollowUp { get; init; }

    public required DateTimeOffset? EndedAt { get; init; }

    public required string RowVersion { get; init; }
}

public sealed record RebalancePortfolioDto(Guid Id, string Name);

public sealed record RebalanceTargetDto(OrderInstrumentDto Instrument, decimal TargetWeight);

/// <summary>R9: a line the plan leaves alone has the six trade fields null.</summary>
public sealed record RebalanceLineDto(
    OrderInstrumentDto Instrument,
    decimal Quantity,
    decimal Weight,
    decimal? TargetWeight,
    string? Side,
    decimal? TradeQuantity,
    decimal? ReferencePrice,
    DateOnly? ReferencePriceDate,
    decimal? EstimatedGross,
    decimal? EstimatedFee,
    string Currency);

public sealed record RebalanceOrderDto(
    Guid Id,
    int Wave,
    string Side,
    OrderInstrumentDto Instrument,
    decimal Quantity,
    decimal FilledQuantity,
    string Status);

public sealed record RebalanceFailureDto(string Code, string Message);

/// <summary>R19: <see cref="By"/> is null for the platform's request.</summary>
public sealed record RebalanceCancelRequestDto(PersonRefDto? By, DateTimeOffset At, string? Note);

/// <summary>R10: the answer of <c>POST /accounts/{id}/rebalance</c> and of assign.</summary>
public sealed record RebalanceProposalDto(string Status, Guid RebalanceId);
