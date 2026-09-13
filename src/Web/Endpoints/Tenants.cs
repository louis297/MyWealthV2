using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Tenants;
using MyWealthV2.Application.Tenants.Queries.GetTenantById;
using MyWealthV2.Application.Tenants.Queries.GetTenants;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MyWealthV2.Web.Endpoints;

public class Tenants : IEndpointGroup
{
    public static string RoutePrefix => "/tenants";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(GetTenants).RequireAuthorization(Policies.TenantsManage);
        groupBuilder.MapGet(GetTenant, "{id}").RequireAuthorization(Policies.TenantsManage);
    }

    [EndpointSummary("List tenants")]
    [EndpointDescription("Returns a paged list of platform tenants. SystemAdmin only.")]
    public static async Task<Ok<PagedList<TenantDto>>> GetTenants(
        ISender sender,
        int page = 1,
        int pageSize = 20,
        string? isEnabled = null,
        string? search = null)
    {
        var result = await sender.Send(new GetTenantsQuery(page, pageSize, isEnabled, search));
        return TypedResults.Ok(result);
    }

    [EndpointSummary("Get a tenant")]
    [EndpointDescription("Returns one tenant by PublicId. SystemAdmin only.")]
    public static async Task<Ok<TenantDto>> GetTenant(ISender sender, Guid id)
    {
        var item = await sender.Send(new GetTenantByIdQuery(id));
        return TypedResults.Ok(item);
    }
}
