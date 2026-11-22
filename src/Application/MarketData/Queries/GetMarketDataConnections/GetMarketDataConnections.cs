using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.MarketData;

namespace MyWealthV2.Application.MarketData.Queries.GetMarketDataConnections;

/// <summary>Simulated instruments R5: the configured market data connections with the state of their price feed,
/// every entry on page 1.</summary>
[Authorize(Policy = Policies.TenantsManage)]
public sealed record GetMarketDataConnectionsQuery : IRequest<PagedList<MarketDataConnectionDto>>;

/// <summary>A connection as the platform may show it: never its URL, key or timeout.</summary>
public sealed record MarketDataConnectionDto(string Name, string Adapter, string SymbolSuffix, MarketDataFeedDto? Prices);

/// <summary>The state of one feed; <c>null</c> until the first read was tried.</summary>
public sealed record MarketDataFeedDto(DateTimeOffset? LastSucceededAt, DateTimeOffset? LastFailedAt, string? LastFailure);

public class GetMarketDataConnectionsQueryHandler(IApplicationDbContext db, MarketDataConnections connections)
    : IRequestHandler<GetMarketDataConnectionsQuery, PagedList<MarketDataConnectionDto>>
{
    private const int DefaultPageSize = 20;

    public async Task<PagedList<MarketDataConnectionDto>> Handle(
        GetMarketDataConnectionsQuery request,
        CancellationToken cancellationToken)
    {
        var names = connections.All.Select(connection => connection.Name).ToList();
        var feeds = await db.MarketDataFeeds.AsNoTracking()
            .Where(feed => feed.Kind == MarketDataFeedKind.Prices && names.Contains(feed.Connection))
            .ToListAsync(cancellationToken);
        var items = connections.All
            .OrderBy(connection => connection.Name, StringComparer.Ordinal)
            .Select(connection =>
            {
                var feed = feeds.SingleOrDefault(row => row.Connection == connection.Name);
                return new MarketDataConnectionDto(
                    connection.Name,
                    connection.Adapter,
                    connection.SymbolSuffix,
                    feed is null
                        ? null
                        : new MarketDataFeedDto(feed.LastSucceededAt, feed.LastFailedAt, feed.LastFailure?.ToString()));
            })
            .ToList();
        return new PagedList<MarketDataConnectionDto>
        {
            Items = items,
            Page = 1,
            PageSize = Math.Max(DefaultPageSize, items.Count),
            TotalCount = items.Count
        };
    }
}
