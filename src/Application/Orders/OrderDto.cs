using System.Text.Json.Serialization;
using MyWealthV2.Application.Common.Models;

namespace MyWealthV2.Application.Orders;

/// <summary>An order item (docs/features/orders.md R15). The list leaves <see cref="Fills"/> out (R16).</summary>
public sealed class OrderDto
{
    public required Guid Id { get; init; }

    public required Guid AccountId { get; init; }

    public required Guid CustomerId { get; init; }

    public required OrderInstrumentDto Instrument { get; init; }

    public required string Side { get; init; }

    public required string Type { get; init; }

    public required string TimeInForce { get; init; }

    public required decimal Quantity { get; init; }

    public required decimal FilledQuantity { get; init; }

    public required decimal OpenQuantity { get; init; }

    /// <summary>The Instrument's quote currency when the order was placed.</summary>
    public required string Currency { get; init; }

    public required decimal ReferencePrice { get; init; }

    public required DateOnly ReferencePriceDate { get; init; }

    public required decimal PriceLimit { get; init; }

    /// <summary>What the order reserves now (R39); 0 when it is not live.</summary>
    public required decimal ReservedCash { get; init; }

    /// <summary>What the order reserves now (R40); 0 when it is not live.</summary>
    public required decimal ReservedQuantity { get; init; }

    /// <summary>The quantity-weighted price of its fills; null with none.</summary>
    public required decimal? AveragePrice { get; init; }

    public required string Status { get; init; }

    /// <summary>The internal note; null for a Customer.</summary>
    public required string? Memo { get; init; }

    public required PersonRefDto PlacedBy { get; init; }

    public required DateTimeOffset PlacedAt { get; init; }

    /// <summary>The send attempts; null for a Customer.</summary>
    public required OrderSendDto? Send { get; init; }

    public required string? RejectReason { get; init; }

    public required OrderCancelRequestDto? CancelRequest { get; init; }

    public required DateTimeOffset? ClosedAt { get; init; }

    /// <summary>The broker's text on a Cancelled or Expired order.</summary>
    public required string? CloseReason { get; init; }

    /// <summary>The fills, oldest first; left out of the list.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<OrderFillDto>? Fills { get; init; }

    public required string RowVersion { get; init; }
}

public sealed record OrderInstrumentDto(Guid Id, string Symbol, string Name);

/// <summary>The send attempts of an order (R20, R21). The failure is a name of <c>BrokerFailure</c>.</summary>
public sealed record OrderSendDto(int Attempts, DateTimeOffset? LastAttemptAt, string? LastFailure, DateTimeOffset? AcceptedAt);

/// <summary>A cancel request (R18). <see cref="By"/> is null for a request the platform made (R50); the note is null
/// for a Customer.</summary>
public sealed record OrderCancelRequestDto(PersonRefDto? By, DateTimeOffset At, string? Note);

/// <summary>A fill of an order (R26).</summary>
public sealed record OrderFillDto(
    Guid Id,
    decimal Quantity,
    decimal Price,
    DateOnly TradeDate,
    DateOnly SettlementDate,
    string? BrokerReference,
    string Status,
    string? HoldReason,
    Guid? TransactionId,
    DateTimeOffset? PostedAt,
    DateTimeOffset? SettledAt,
    string? BrokerSettlementId = null,
    Guid? TradeSettlementId = null,
    OrderFillBreakDto? Break = null);

/// <summary>Trade-settlement R34: the break of a fill. A Customer sees it without <see cref="Note"/>.</summary>
public sealed record OrderFillBreakDto(
    DateTimeOffset At,
    string? Reason,
    string Outcome,
    string? HoldReason,
    Guid? ReversalTransactionId,
    PersonRefDto? ResolvedBy,
    DateTimeOffset? ResolvedAt,
    string? Note);
