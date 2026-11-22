using Microsoft.AspNetCore.Http.HttpResults;
using MyWealthV2.Application.Broker.Queries;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Web.Endpoints;

/// <summary>Configured broker connections (docs/features/orders.md R4).</summary>
public class BrokerConnections : IEndpointGroup
{
    public static string RoutePrefix => "/broker-connections";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(GetBrokerConnections).RequireAuthorization(Policies.TenantsManage);
    }

    [EndpointSummary("List broker connections")]
    [EndpointDescription("Returns the configured broker connections with the state of their execution report feed: name, adapter and reports. SystemAdmin only.")]
    public static async Task<Ok<PagedList<BrokerConnectionDto>>> GetBrokerConnections(ISender sender)
    {
        var result = await sender.Send(new GetBrokerConnectionsQuery());
        return TypedResults.Ok(result);
    }
}
