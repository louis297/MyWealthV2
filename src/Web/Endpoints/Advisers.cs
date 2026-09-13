using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Users;
using MyWealthV2.Application.Users.Commands.CreateAdviser;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MyWealthV2.Web.Endpoints;

public class Advisers : IEndpointGroup
{
    public static string RoutePrefix => "/users/advisers";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapPost(CreateAdviser).RequireAuthorization(Policies.AdvisersManage);
    }

    [EndpointSummary("Create an adviser")]
    [EndpointDescription("Creates an Adviser with a password in the current tenant. TenantAdmin only.")]
    public static async Task<Created<CreatedAdviserDto>> CreateAdviser(
        ISender sender,
        CreateAdviserCommand command)
    {
        var id = await sender.Send(command);
        return TypedResults.Created($"/users/advisers/{id}", new CreatedAdviserDto(id));
    }
}
