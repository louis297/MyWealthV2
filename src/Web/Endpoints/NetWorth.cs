using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.NetWorth;
using MyWealthV2.Application.NetWorth.Queries.GetFundsUnderManagement;
using MyWealthV2.Application.NetWorth.Queries.GetNetWorth;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MyWealthV2.Web.Endpoints;

public class NetWorth : IEndpointGroup
{
    public static string RoutePrefix => "/net-worth";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(GetNetWorth).RequireAuthorization(Policies.AccountsRead);
        groupBuilder.MapGet(GetFundsUnderManagement, "funds-under-management").RequireAuthorization(Policies.AccountsRead);
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

    [EndpointSummary("Get the funds under management series")]
    [EndpointDescription("Returns the funds under management of the caller's scope for every day from `from` to `to`, from the stored valuations, per currency and folded into the Tenant's reporting currency. Custodial accounts only; days up to the last final day.")]
    public static async Task<Ok<FundsUnderManagementDto>> GetFundsUnderManagement(
        ISender sender,
        Guid? tenantId = null,
        string? from = null,
        string? to = null)
    {
        var result = await sender.Send(new GetFundsUnderManagementQuery(tenantId, from, to));
        return TypedResults.Ok(result);
    }
}
