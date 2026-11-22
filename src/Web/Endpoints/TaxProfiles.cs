using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Interest.TaxProfiles;

namespace MyWealthV2.Web.Endpoints;

/// <summary>Interest-run §8: the Customer tax profile, a sub-resource of <c>/users/customers/{customerId}</c>.</summary>
public class TaxProfiles : IEndpointGroup
{
    public static string RoutePrefix => "/users/customers";

    private static readonly AuthorizationPolicy ManageOrOwn = new AuthorizationPolicyBuilder()
        .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()
        .AddRequirements(new AnyPermissionRequirement(Policies.CustomersManage, Policies.CustomersManageOwn))
        .Build();

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(GetCustomerTaxProfile, "{customerId:guid}/tax-profile").RequireAuthorization(ManageOrOwn);
        groupBuilder.MapPut(SetCustomerTaxProfile, "{customerId:guid}/tax-profile").RequireAuthorization(ManageOrOwn);
    }

    [EndpointSummary("Get a customer's tax profile")]
    [EndpointDescription("Tax residency, IRD number held and the chosen RWT rate, with the withholding they give. A Customer with nothing saved reads as not provided.")]
    public static async Task<Ok<TaxProfileDto>> GetCustomerTaxProfile(ISender sender, Guid customerId) =>
        TypedResults.Ok(await sender.Send(new GetCustomerTaxProfileQuery(customerId)));

    [EndpointSummary("Set a customer's tax profile")]
    [EndpointDescription("Replaces the three attributes. rowVersion is the one the read returned, null for the first write.")]
    public static async Task<NoContent> SetCustomerTaxProfile(ISender sender, Guid customerId, SetCustomerTaxProfileCommand command)
    {
        command.Path = customerId;
        await sender.Send(command);
        return TypedResults.NoContent();
    }
}
