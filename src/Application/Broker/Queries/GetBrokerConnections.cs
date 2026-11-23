using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Application.Broker.Queries;

/// <summary>Orders R4: the configured broker connections with the state of their execution report feed, every entry
/// on page 1.</summary>
[Authorize(Policy = Policies.TenantsManage)]
public sealed record GetBrokerConnectionsQuery : IRequest<PagedList<BrokerConnectionDto>>;

/// <summary>A connection as the platform may show it: never its URL, key or timeout. <see cref="Reports"/> is the
/// state of its execution report feed; <see cref="SettlementCurrencies"/> the currencies it has a broker bank account
/// in (trade-settlement R2).</summary>
public sealed record BrokerConnectionDto(
    string Name,
    string Adapter,
    BrokerFeedDto? Reports,
    IReadOnlyList<string> SettlementCurrencies);

/// <summary>The state of the feed; <c>null</c> on the connection until the first read was tried. The failure is a
/// name of <c>BrokerFailure</c>, never a message from the party.</summary>
public sealed record BrokerFeedDto(DateTimeOffset? LastSucceededAt, DateTimeOffset? LastFailedAt, string? LastFailure);

public class GetBrokerConnectionsQueryHandler(IApplicationDbContext db, IBrokerConnections connections)
    : IRequestHandler<GetBrokerConnectionsQuery, PagedList<BrokerConnectionDto>>
{
    private const int DefaultPageSize = 20;

    public async Task<PagedList<BrokerConnectionDto>> Handle(GetBrokerConnectionsQuery request, CancellationToken cancellationToken)
    {
        var names = connections.All.Select(connection => connection.Name).ToList();
        var feeds = await db.BrokerFeeds.AsNoTracking()
            .Where(feed => names.Contains(feed.Connection))
            .ToDictionaryAsync(feed => feed.Connection, cancellationToken);
        var items = connections.All
            .OrderBy(connection => connection.Name, StringComparer.Ordinal)
            .Select(connection => new BrokerConnectionDto(
                connection.Name,
                connection.Adapter,
                feeds.TryGetValue(connection.Name, out var feed)
                    ? new BrokerFeedDto(feed.LastSucceededAt, feed.LastFailedAt, feed.LastFailure?.ToString())
                    : null,
                connections.SettlementCurrencies(connection.Name)))
            .ToList();
        return new PagedList<BrokerConnectionDto>
        {
            Items = items,
            Page = 1,
            PageSize = Math.Max(DefaultPageSize, items.Count),
            TotalCount = items.Count
        };
    }
}
