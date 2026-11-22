using Microsoft.Extensions.Logging;
using MyWealthV2.Application.Broker;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Infrastructure.Broker;

/// <summary>One adapter of the broker orders port, chosen by a connection's <c>Adapter</c> (orders R66).</summary>
public interface IBrokerOrdersAdapter
{
    string Adapter { get; }

    Task<BrokerPlaceResult> PlaceAsync(BrokerConnectionSettings connection, BrokerOrderRequest order, CancellationToken cancellationToken);

    Task<BrokerCancelResult> CancelAsync(BrokerConnectionSettings connection, string clientOrderId, CancellationToken cancellationToken);
}

/// <summary>One adapter of the broker feed port, chosen by a connection's <c>Adapter</c> (orders R67).</summary>
public interface IBrokerFeedAdapter
{
    string Adapter { get; }

    Task<BrokerFeedResult> GetReportsAsync(BrokerConnectionSettings connection, string? cursor, int limit, CancellationToken cancellationToken);
}

/// <summary>
/// <see cref="IBrokerOrders"/> for the hosts: sends each call to the adapter its connection names (ADR 0021 rule 3).
/// </summary>
public sealed class BrokerOrdersDispatcher : IBrokerOrders
{
    public BrokerOrdersDispatcher(
        ConfiguredBrokerConnections connections,
        IEnumerable<IBrokerOrdersAdapter> adapters,
        ILogger<BrokerOrdersDispatcher> logger)
    {
        _ = (connections, adapters, logger);
    }

    public Task<BrokerPlaceResult> PlaceAsync(string connection, BrokerOrderRequest order, CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public Task<BrokerCancelResult> CancelAsync(string connection, string clientOrderId, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}

/// <summary>
/// <see cref="IBrokerFeed"/> for the hosts: sends each call to the adapter its connection names (ADR 0021 rule 3).
/// </summary>
public sealed class BrokerFeedDispatcher : IBrokerFeed
{
    public BrokerFeedDispatcher(
        ConfiguredBrokerConnections connections,
        IEnumerable<IBrokerFeedAdapter> adapters,
        ILogger<BrokerFeedDispatcher> logger)
    {
        _ = (connections, adapters, logger);
    }

    public Task<BrokerFeedResult> GetReportsAsync(string connection, string? cursor, int limit, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
