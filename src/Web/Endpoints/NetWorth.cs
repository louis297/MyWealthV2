using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.NetWorth;
using MyWealthV2.Application.NetWorth.Queries.GetNetWorth;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MyWealthV2.Web.Endpoints;

public class NetWorth : IEndpointGroup
{
    public static string RoutePrefix => "/net-worth";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(GetNetWorth).RequireAuthorization(Policies.AccountsRead);
    }

    [EndpointSummary("Get current net worth")]
    [EndpointDescription("Returns the current per-currency net worth for the caller’s scope. Open accounts only.")]
    public static async Task<Ok<NetWorthDto>> GetNetWorth(
        ISender sender,
        Guid? tenantId = null,
        Guid? customerId = null)
    {
        var result = await sender.Send(new GetNetWorthQuery(tenantId, customerId));
        return TypedResults.Ok(result);
    }
}
