using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.MarketData.Queries.GetMarketDataConnections;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MyWealthV2.Web.Endpoints;

/// <summary>Configured market data connections (docs/features/simulated-instruments.md R5).</summary>
public class MarketDataConnections : IEndpointGroup
{
    public static string RoutePrefix => "/market-data-connections";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(GetMarketDataConnections).RequireAuthorization(Policies.TenantsManage);
    }

    [EndpointSummary("List market data connections")]
    [EndpointDescription("Returns the configured market data connections: name, adapter, symbol suffix and the state of the price feed. No URL or key. SystemAdmin only.")]
    public static async Task<Ok<PagedList<MarketDataConnectionDto>>> GetMarketDataConnections(ISender sender)
    {
        var result = await sender.Send(new GetMarketDataConnectionsQuery());
        return TypedResults.Ok(result);
    }
}
