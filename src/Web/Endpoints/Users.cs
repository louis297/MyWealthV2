using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Users;
using MyWealthV2.Application.Users.Queries.GetCurrentUser;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MyWealthV2.Web.Endpoints;

public class Users : IEndpointGroup
{
    public static string RoutePrefix => "/users";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(GetMe, "me").RequireAuthorization(Policies.UsersMe);
    }

    [EndpointSummary("Get the current user")]
    [EndpointDescription("Returns the authenticated person's profile. Does not issue tokens.")]
    public static async Task<Ok<CurrentUserDto>> GetMe(ISender sender)
    {
        var profile = await sender.Send(new GetCurrentUserQuery());
        return TypedResults.Ok(profile);
    }
}
