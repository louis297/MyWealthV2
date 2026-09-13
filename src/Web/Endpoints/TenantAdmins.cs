using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Users;
using MyWealthV2.Application.Users.Commands.CreateTenantAdmin;
using MyWealthV2.Application.Users.Commands.DisableTenantAdmin;
using MyWealthV2.Application.Users.Commands.EnableTenantAdmin;
using MyWealthV2.Application.Users.Commands.UpdateTenantAdmin;
using MyWealthV2.Application.Users.Queries.GetTenantAdminById;
using MyWealthV2.Application.Users.Queries.GetTenantAdmins;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MyWealthV2.Web.Endpoints;

public class TenantAdmins : IEndpointGroup
{
    public static string RoutePrefix => "/users/tenant-admins";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(GetTenantAdmins).RequireAuthorization(Policies.TenantAdminsManage);
        groupBuilder.MapGet(GetTenantAdmin, "{id}").RequireAuthorization(Policies.TenantAdminsManage);
        groupBuilder.MapPost(CreateTenantAdmin).RequireAuthorization(Policies.TenantAdminsManage);
        groupBuilder.MapPut(UpdateTenantAdmin, "{id}").RequireAuthorization(Policies.TenantAdminsManage);
        groupBuilder.MapPost(DisableTenantAdmin, "{id}/disable").RequireAuthorization(Policies.TenantAdminsManage);
        groupBuilder.MapPost(EnableTenantAdmin, "{id}/enable").RequireAuthorization(Policies.TenantAdminsManage);
    }

    [EndpointSummary("List tenant admins")]
    [EndpointDescription("Returns a paged list of TenantAdmins. SystemAdmin only.")]
    public static async Task<Ok<PagedList<TenantAdminDto>>> GetTenantAdmins(
        ISender sender,
        int page = 1,
        int pageSize = 20,
        string? tenantId = null,
        string? enabledOnly = null,
        string? search = null)
    {
        var result = await sender.Send(new GetTenantAdminsQuery(page, pageSize, tenantId, enabledOnly, search));
        return TypedResults.Ok(result);
    }

    [EndpointSummary("Get a tenant admin")]
    [EndpointDescription("Returns one TenantAdmin by PublicId. SystemAdmin only.")]
    public static async Task<Ok<TenantAdminDto>> GetTenantAdmin(ISender sender, Guid id)
    {
        var item = await sender.Send(new GetTenantAdminByIdQuery(id));
        return TypedResults.Ok(item);
    }

    [EndpointSummary("Create a tenant admin")]
    [EndpointDescription("Creates a TenantAdmin with a password on an enabled tenant. SystemAdmin only.")]
    public static async Task<Created<CreatedTenantAdminDto>> CreateTenantAdmin(
        ISender sender,
        CreateTenantAdminCommand command)
    {
        var id = await sender.Send(command);
        return TypedResults.Created($"/users/tenant-admins/{id}", new CreatedTenantAdminDto(id));
    }

    [EndpointSummary("Update a tenant admin")]
    [EndpointDescription("Renames a TenantAdmin. Email, tenant, and role cannot change. SystemAdmin only.")]
    public static async Task<NoContent> UpdateTenantAdmin(
        ISender sender,
        Guid id,
        UpdateTenantAdminCommand command)
    {
        command.Id = id;
        await sender.Send(command);
        return TypedResults.NoContent();
    }

    [EndpointSummary("Disable a tenant admin")]
    [EndpointDescription("Disables a TenantAdmin. Idempotent. Last TenantAdmin may be disabled. SystemAdmin only.")]
    public static async Task<NoContent> DisableTenantAdmin(
        ISender sender,
        Guid id,
        DisableTenantAdminCommand command)
    {
        command.Id = id;
        await sender.Send(command);
        return TypedResults.NoContent();
    }

    [EndpointSummary("Enable a tenant admin")]
    [EndpointDescription("Re-enables a TenantAdmin. Idempotent. SystemAdmin only.")]
    public static async Task<NoContent> EnableTenantAdmin(
        ISender sender,
        Guid id,
        EnableTenantAdminCommand command)
    {
        command.Id = id;
        await sender.Send(command);
        return TypedResults.NoContent();
    }
}
