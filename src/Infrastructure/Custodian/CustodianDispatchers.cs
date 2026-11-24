using Microsoft.Extensions.Logging;
using MyWealthV2.Application.Custodian;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Infrastructure.Custodian;

/// <summary>One adapter of the custodian feed port, chosen by a connection's <c>Adapter</c> (in-kind-transfers R73).</summary>
public interface ICustodianFeedAdapter
{
    string Adapter { get; }

    Task<CustodianFeedResult> GetMovementsAsync(
        CustodianConnectionSettings connection, CustodianFeedRequest request, CancellationToken cancellationToken);
}

/// <summary>One adapter of the custodian transfer port, chosen by a connection's <c>Adapter</c> (in-kind-transfers R74).</summary>
public interface ICustodianTransfersAdapter
{
    string Adapter { get; }

    Task<CustodianTransferResult> SendAsync(
        CustodianConnectionSettings connection, CustodianTransferRequest request, CancellationToken cancellationToken);
}

/// <summary>One adapter of the custodian elections port, chosen by a connection's <c>Adapter</c> (dividends R65).</summary>
public interface ICustodianElectionsAdapter
{
    string Adapter { get; }

    Task<CustodianElectionResult> SubmitAsync(
        CustodianConnectionSettings connection, CustodianElectionRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// <see cref="ICustodianFeed"/> for the hosts: reads each page through the adapter its connection names (ADR 0021
/// rule 3). A connection that is not configured on this host, or names no registered adapter, is unreachable.
/// </summary>
public sealed class CustodianFeedDispatcher(
    ConfiguredCustodianConnections connections,
    IEnumerable<ICustodianFeedAdapter> adapters,
    ILogger<CustodianFeedDispatcher> logger) : ICustodianFeed
{
    public Task<CustodianFeedResult> GetMovementsAsync(CustodianFeedRequest request, CancellationToken cancellationToken)
    {
        var settings = connections.Find(request.Connection);
        var adapter = settings is null ? null : adapters.SingleOrDefault(entry => entry.Adapter == settings.Adapter);
        if (settings is null || adapter is null)
        {
            logger.LogWarning("Custodian connection {Connection} has no feed adapter on this host.", request.Connection);
            return Task.FromResult(CustodianFeedResult.Failed(CustodianFailure.Unreachable));
        }

        return adapter.GetMovementsAsync(settings, request, cancellationToken);
    }
}

/// <summary><see cref="ICustodianTransfers"/> for the hosts, dispatched as <see cref="CustodianFeedDispatcher"/>.</summary>
public sealed class CustodianTransfersDispatcher(
    ConfiguredCustodianConnections connections,
    IEnumerable<ICustodianTransfersAdapter> adapters,
    ILogger<CustodianTransfersDispatcher> logger) : ICustodianTransfers
{
    public Task<CustodianTransferResult> SendAsync(CustodianTransferRequest request, CancellationToken cancellationToken)
    {
        var settings = connections.Find(request.Connection);
        var adapter = settings is null ? null : adapters.SingleOrDefault(entry => entry.Adapter == settings.Adapter);
        if (settings is null || adapter is null)
        {
            logger.LogWarning("Custodian connection {Connection} has no transfers adapter on this host.", request.Connection);
            return Task.FromResult(CustodianTransferResult.Failed(CustodianFailure.Unreachable));
        }

        return adapter.SendAsync(settings, request, cancellationToken);
    }
}

/// <summary><see cref="ICustodianElections"/> for the hosts, dispatched as <see cref="CustodianFeedDispatcher"/>.</summary>
public sealed class CustodianElectionsDispatcher(
    ConfiguredCustodianConnections connections,
    IEnumerable<ICustodianElectionsAdapter> adapters,
    ILogger<CustodianElectionsDispatcher> logger) : ICustodianElections
{
    public Task<CustodianElectionResult> SubmitAsync(CustodianElectionRequest request, CancellationToken cancellationToken)
    {
        var settings = connections.Find(request.Connection);
        var adapter = settings is null ? null : adapters.SingleOrDefault(entry => entry.Adapter == settings.Adapter);
        if (settings is null || adapter is null)
        {
            logger.LogWarning("Custodian connection {Connection} has no elections adapter on this host.", request.Connection);
            return Task.FromResult(CustodianElectionResult.Failed(CustodianFailure.Unreachable));
        }

        return adapter.SubmitAsync(settings, request, cancellationToken);
    }
}
