using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using MyWealthV2.Application.CorporateActions;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Infrastructure.MarketData;

/// <summary>
/// The corporate action feed adapter for <c>Adapter = Simulator</c> (docs/features/corporate-actions.md R48): calls the
/// simulator market's feed route (R44) after a cursor with the connection's key and timeout. It keeps its own wire
/// types (ADR 0021, no shared contracts). A field it cannot read comes through empty, so the pull refuses the page;
/// an unknown kind comes through as no kind. Every failure comes back as a result.
/// </summary>
public sealed class SimulatorCorporateActionFeed : ICorporateActionFeedAdapter
{
    public const string HttpClientName = "corporate-action-feed-simulator";

    public SimulatorCorporateActionFeed(IHttpClientFactory httpClients)
    {
        _ = httpClients;
    }

    public string Adapter => MarketDataConnectionsConfiguration.SimulatorAdapter;

    public Task<CorporateActionFeedResult> GetAnnouncementsAsync(
        MarketDataConnectionSettings connection,
        string? cursor,
        int limit,
        CancellationToken cancellationToken) => throw new NotImplementedException();
}
