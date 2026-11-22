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
/// A connection that is not configured on this host, or names no registered adapter, is unreachable.
/// </summary>
public sealed class BrokerOrdersDispatcher(
    ConfiguredBrokerConnections connections,
    IEnumerable<IBrokerOrdersAdapter> adapters,
    ILogger<BrokerOrdersDispatcher> logger) : IBrokerOrders
{
    public Task<BrokerPlaceResult> PlaceAsync(string connection, BrokerOrderRequest order, CancellationToken cancellationToken)
    {
        var (settings, adapter) = Find(connection);
        return settings is null || adapter is null
            ? Task.FromResult(BrokerPlaceResult.Failed(BrokerFailure.Unreachable))
            : adapter.PlaceAsync(settings, order, cancellationToken);
    }

    public Task<BrokerCancelResult> CancelAsync(string connection, string clientOrderId, CancellationToken cancellationToken)
    {
        var (settings, adapter) = Find(connection);
        return settings is null || adapter is null
            ? Task.FromResult(BrokerCancelResult.Failed(BrokerFailure.Unreachable))
            : adapter.CancelAsync(settings, clientOrderId, cancellationToken);
    }

    private (BrokerConnectionSettings? Settings, IBrokerOrdersAdapter? Adapter) Find(string connection)
    {
        var settings = connections.Settings.SingleOrDefault(entry => entry.Name == connection);
        var adapter = settings is null ? null : adapters.SingleOrDefault(entry => entry.Adapter == settings.Adapter);
        if (settings is null || adapter is null)
        {
            logger.LogWarning("Broker connection {Connection} has no orders adapter on this host.", connection);
        }

        return (settings, adapter);
    }
}

/// <summary>
/// <see cref="IBrokerFeed"/> for the hosts: sends each call to the adapter its connection names (ADR 0021 rule 3).
/// A connection that is not configured on this host, or names no registered adapter, is unreachable.
/// </summary>
public sealed class BrokerFeedDispatcher(
    ConfiguredBrokerConnections connections,
    IEnumerable<IBrokerFeedAdapter> adapters,
    ILogger<BrokerFeedDispatcher> logger) : IBrokerFeed
{
    public Task<BrokerFeedResult> GetReportsAsync(string connection, string? cursor, int limit, CancellationToken cancellationToken)
    {
        var settings = connections.Settings.SingleOrDefault(entry => entry.Name == connection);
        var adapter = settings is null ? null : adapters.SingleOrDefault(entry => entry.Adapter == settings.Adapter);
        if (settings is null || adapter is null)
        {
            logger.LogWarning("Broker connection {Connection} has no feed adapter on this host.", connection);
            return Task.FromResult(BrokerFeedResult.Failed(BrokerFailure.Unreachable));
        }

        return adapter.GetReportsAsync(settings, cursor, limit, cancellationToken);
    }
}
