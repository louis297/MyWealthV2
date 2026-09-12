using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Users;
using MyWealthV2.Application.Users.Commands.ChangeCurrentUserPassword;
using MyWealthV2.Application.Users.Commands.UpdateCurrentUser;
using MyWealthV2.Application.Users.Queries.GetCurrentUser;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MyWealthV2.Web.Endpoints;

public class Users : IEndpointGroup
{
    public static string RoutePrefix => "/users";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(GetMe, "me").RequireAuthorization(Policies.UsersMe);
        groupBuilder.MapPut(UpdateMe, "me").RequireAuthorization(Policies.UsersMe);
        groupBuilder.MapPut(ChangePassword, "me/password").RequireAuthorization(Policies.UsersMe);
    }

    [EndpointSummary("Get the current user")]
    [EndpointDescription("Returns the authenticated person's profile. Does not issue tokens.")]
    public static async Task<Ok<CurrentUserDto>> GetMe(ISender sender)
    {
        var profile = await sender.Send(new GetCurrentUserQuery());
        return TypedResults.Ok(profile);
    }

    [EndpointSummary("Update the current user")]
    [EndpointDescription("Updates the authenticated person's display name only.")]
    public static async Task<NoContent> UpdateMe(ISender sender, UpdateCurrentUserCommand command)
    {
        await sender.Send(command);
        return TypedResults.NoContent();
    }

    [EndpointSummary("Change the current user password")]
    [EndpointDescription("Changes the password after verifying the current one. Does not return tokens.")]
    public static async Task<NoContent> ChangePassword(ISender sender, ChangeCurrentUserPasswordCommand command)
    {
        await sender.Send(command);
        return TypedResults.NoContent();
    }
}
