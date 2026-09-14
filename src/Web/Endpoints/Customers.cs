using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Users;
using MyWealthV2.Application.Users.Commands.CreateCustomer;
using MyWealthV2.Application.Users.Queries.GetCustomerById;
using MyWealthV2.Application.Users.Queries.GetCustomers;
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
        groupBuilder.MapGet(GetCustomers).RequireAuthorization(ManageOrOwn);
        groupBuilder.MapGet(GetCustomer, "{id}").RequireAuthorization(ManageOrOwn);
        groupBuilder.MapPost(CreateCustomer).RequireAuthorization(ManageOrOwn);
    }

    [EndpointSummary("List customers")]
    [EndpointDescription("Returns a paged list of Customers. TenantAdmin sees the tenant; Adviser sees assigned only.")]
    public static async Task<Ok<PagedList<CustomerDto>>> GetCustomers(
        ISender sender,
        int page = 1,
        int pageSize = 20,
        string? enabledOnly = null,
        string? search = null,
        Guid? adviserId = null)
    {
        var result = await sender.Send(new GetCustomersQuery(page, pageSize, enabledOnly, search, adviserId));
        return TypedResults.Ok(result);
    }

    [EndpointSummary("Get a customer")]
    [EndpointDescription("Returns one Customer by PublicId. TenantAdmin or the assigned Adviser.")]
    public static async Task<Ok<CustomerDto>> GetCustomer(ISender sender, Guid id)
    {
        var item = await sender.Send(new GetCustomerByIdQuery(id));
        return TypedResults.Ok(item);
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
