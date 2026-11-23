using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.Broker;

/// <summary>
/// The broker orders port (docs/features/orders.md R66; ADR 0021 rules 12–14): place one order and ask to cancel one
/// order on a broker connection. The client order id is the idempotency key the broker sees on every attempt.
/// Infrastructure dispatches to the adapter of the connection. A failure comes back as a result, never as an
/// exception the caller must parse.
/// </summary>
public interface IBrokerOrders
{
    Task<BrokerPlaceResult> PlaceAsync(string connection, BrokerOrderRequest order, CancellationToken cancellationToken);

    Task<BrokerCancelResult> CancelAsync(string connection, string clientOrderId, CancellationToken cancellationToken);
}

/// <summary>One order as the broker sees it, built from the stored order only (R20, R66). It names no Tenant, Account
/// or Customer.</summary>
public sealed record BrokerOrderRequest(
    string ClientOrderId,
    string Symbol,
    string Currency,
    OrderSide Side,
    decimal Quantity,
    OrderType Type,
    TimeInForce TimeInForce,
    decimal PriceLimit,
    decimal ReferencePrice,
    string? SettlementAccount = null);

/// <summary>The broker's answer to a place call (R21): accepted with its order id, rejected with a reason, or a
/// failure of the call itself.</summary>
public sealed class BrokerPlaceResult
{
    private BrokerPlaceResult(string? brokerOrderId, OrderRejectReason? rejectReason, BrokerFailure? failure)
    {
        BrokerOrderId = brokerOrderId;
        RejectReason = rejectReason;
        Failure = failure;
    }

    public string? BrokerOrderId { get; }

    public OrderRejectReason? RejectReason { get; }

    public BrokerFailure? Failure { get; }

    public static BrokerPlaceResult Accepted(string brokerOrderId) => new(brokerOrderId, null, null);

    public static BrokerPlaceResult Rejected(OrderRejectReason reason) => new(null, reason, null);

    public static BrokerPlaceResult Failed(BrokerFailure failure) => new(null, null, failure);
}

/// <summary>How the broker answered a cancel call (R22).</summary>
public enum BrokerCancelAnswer
{
    /// <summary>The broker cancels the order; its <c>Cancelled</c> report ends it.</summary>
    Accepted,

    /// <summary>The order is filled, cancelled, expired or rejected already; the reports say how it ended.</summary>
    TooLate,

    /// <summary>The broker never had the order.</summary>
    UnknownOrder
}

/// <summary>The broker's answer to a cancel call (R22), or a failure of the call itself.</summary>
public sealed class BrokerCancelResult
{
    private BrokerCancelResult(BrokerCancelAnswer? answer, BrokerFailure? failure)
    {
        Answer = answer;
        Failure = failure;
    }

    public BrokerCancelAnswer? Answer { get; }

    public BrokerFailure? Failure { get; }

    public static BrokerCancelResult Answered(BrokerCancelAnswer answer) => new(answer, null);

    public static BrokerCancelResult Failed(BrokerFailure failure) => new(null, failure);
}
