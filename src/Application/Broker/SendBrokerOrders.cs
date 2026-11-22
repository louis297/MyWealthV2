using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Application.Broker;

/// <summary>What a step of the broker sync did: how many orders it handled and how many calls failed or were left
/// for the next run.</summary>
public sealed record BrokerStepOutcome(int Handled, int Failed)
{
    public static BrokerStepOutcome None { get; } = new(0, 0);
}

/// <summary>
/// The send step and the cancel step of <c>broker-sync:{connection}</c> (docs/features/orders.md R20–R22; ADR 0021
/// rules 12, 13).
/// </summary>
public sealed class SendBrokerOrders
{
    /// <summary>Both steps, in order: the orders (R20), then the cancel requests (R22).</summary>
    public Task<BrokerStepOutcome> RunAsync(string connection, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    /// <summary>R20, R21: every <c>Placed</c> order of the connection with no cancel request, oldest first.</summary>
    public Task<BrokerStepOutcome> SendAsync(string connection, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    /// <summary>R22: every open order of the connection with a cancel request the broker has not answered, oldest
    /// first.</summary>
    public Task<BrokerStepOutcome> CancelAsync(string connection, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    /// <summary>R20: the order's PublicId is the client order id on every attempt.</summary>
    public static string ClientOrderId(Order order) => throw new NotImplementedException();
}
