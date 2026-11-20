using MyWealthV2.Application.BankFeed.Queries.GetBankConnections;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MyWealthV2.Web.Endpoints;

/// <summary>Configured bank connections (docs/features/deposit-matching.md R3).</summary>
public class BankConnections : IEndpointGroup
{
    public static string RoutePrefix => "/bank-connections";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(GetBankConnections).RequireAuthorization(Policies.TenantsManage);
    }

    [EndpointSummary("List bank connections")]
    [EndpointDescription("Returns the configured bank connections, name and adapter only. SystemAdmin only.")]
    public static async Task<Ok<PagedList<BankConnectionDto>>> GetBankConnections(ISender sender)
    {
        var result = await sender.Send(new GetBankConnectionsQuery());
        return TypedResults.Ok(result);
    }
}
