using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Users;
using MyWealthV2.Application.Users.Commands.CreateTenantAdmin;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MyWealthV2.Web.Endpoints;

public class TenantAdmins : IEndpointGroup
{
    public static string RoutePrefix => "/users/tenant-admins";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapPost(CreateTenantAdmin).RequireAuthorization(Policies.TenantAdminsManage);
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
}
