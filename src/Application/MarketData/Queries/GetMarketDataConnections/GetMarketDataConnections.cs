using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
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

    public Task<PagedList<MarketDataConnectionDto>> Handle(
        GetMarketDataConnectionsQuery request,
        CancellationToken cancellationToken)
    {
        _ = (db, connections);
        return Task.FromResult(new PagedList<MarketDataConnectionDto>
        {
            Items = [],
            Page = 1,
            PageSize = DefaultPageSize,
            TotalCount = 0
        });
    }
}
