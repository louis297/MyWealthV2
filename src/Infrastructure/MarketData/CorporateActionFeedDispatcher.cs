using Microsoft.Extensions.Logging;
using MyWealthV2.Application.CorporateActions;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Infrastructure.MarketData;

/// <summary>One adapter of the corporate action feed port, chosen by a connection's <c>Adapter</c>
/// (docs/features/corporate-actions.md R48).</summary>
public interface ICorporateActionFeedAdapter
{
    string Adapter { get; }

    Task<CorporateActionFeedResult> GetAnnouncementsAsync(
        MarketDataConnectionSettings connection,
        string? cursor,
        int limit,
        CancellationToken cancellationToken);
}

/// <summary>
/// <see cref="ICorporateActionFeed"/> for the hosts: sends each call to the adapter its connection names (ADR 0021
/// rule 3). A connection that is not configured on this host, or names no registered adapter, is unreachable.
/// </summary>
public sealed class CorporateActionFeedDispatcher(
    ConfiguredMarketDataConnections connections,
    IEnumerable<ICorporateActionFeedAdapter> adapters,
    ILogger<CorporateActionFeedDispatcher> logger) : ICorporateActionFeed
{
    public Task<CorporateActionFeedResult> GetAnnouncementsAsync(
        string connection,
        string? cursor,
        int limit,
        CancellationToken cancellationToken)
    {
        var settings = connections.Settings.SingleOrDefault(entry => entry.Name == connection);
        var adapter = settings is null ? null : adapters.SingleOrDefault(entry => entry.Adapter == settings.Adapter);
        if (settings is null || adapter is null)
        {
            logger.LogWarning("Market data connection {Connection} has no corporate action feed adapter on this host.", connection);
            return Task.FromResult(CorporateActionFeedResult.Failed(MarketDataFailure.Unreachable));
        }

        return adapter.GetAnnouncementsAsync(settings, cursor, limit, cancellationToken);
    }
}
