using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Users;
using MyWealthV2.Application.Users.Commands.CreateCustomer;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MyWealthV2.Web.Endpoints;

public class Customers : IEndpointGroup
{
    public static string RoutePrefix => "/users/customers";

    private static readonly AuthorizationPolicy ManageOrOwn = new AuthorizationPolicyBuilder()
        .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()
        .AddRequirements(new AnyPermissionRequirement(Policies.CustomersManage, Policies.CustomersManageOwn))
        .Build();

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapPost(CreateCustomer).RequireAuthorization(ManageOrOwn);
    }

    [EndpointSummary("Create a customer")]
    [EndpointDescription("Creates a Customer with a password in the current tenant. TenantAdmin or assigned Adviser.")]
    public static async Task<Created<CreatedCustomerDto>> CreateCustomer(
        ISender sender,
        CreateCustomerCommand command)
    {
        var id = await sender.Send(command);
        return TypedResults.Created($"/users/customers/{id}", new CreatedCustomerDto(id));
    }
}
