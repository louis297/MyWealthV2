using Microsoft.AspNetCore.Http.HttpResults;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.SafekeepingAccounts;

namespace MyWealthV2.Web.Endpoints;

/// <summary>Safekeeping accounts of one tenant (docs/features/in-kind-transfers.md R3–R7).</summary>
public class SafekeepingAccounts : IEndpointGroup
{
    public static string RoutePrefix => "/tenants/{tenantId}/safekeeping-accounts";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(GetSafekeepingAccounts).RequireAuthorization(Policies.TenantsManage);
        groupBuilder.MapGet(GetSafekeepingAccount, "{id}").RequireAuthorization(Policies.TenantsManage);
        groupBuilder.MapPost(CreateSafekeepingAccount).RequireAuthorization(Policies.TenantsManage);
        groupBuilder.MapPost(DisableSafekeepingAccount, "{id}/disable").RequireAuthorization(Policies.TenantsManage);
        groupBuilder.MapPost(EnableSafekeepingAccount, "{id}/enable").RequireAuthorization(Policies.TenantsManage);
    }

    [EndpointSummary("List safekeeping accounts")]
    [EndpointDescription("Returns a paged list of one tenant's safekeeping accounts with their feed status. SystemAdmin only.")]
    public static async Task<Ok<PagedList<SafekeepingAccountDto>>> GetSafekeepingAccounts(
        ISender sender, Guid tenantId, int page = 1, int pageSize = 20) =>
        TypedResults.Ok(await sender.Send(new GetSafekeepingAccountsQuery(tenantId, page, pageSize)));

    [EndpointSummary("Get a safekeeping account")]
    [EndpointDescription("Returns one safekeeping account of the tenant by PublicId. SystemAdmin only.")]
    public static async Task<Ok<SafekeepingAccountDto>> GetSafekeepingAccount(ISender sender, Guid tenantId, Guid id) =>
        TypedResults.Ok(await sender.Send(new GetSafekeepingAccountByIdQuery(tenantId, id)));

    [EndpointSummary("Create a safekeeping account")]
    [EndpointDescription("Records the tenant's pooled account at a custodian for the securities quoted in one currency. SystemAdmin only.")]
    public static async Task<Created<CreatedSafekeepingAccountDto>> CreateSafekeepingAccount(
        ISender sender, Guid tenantId, CreateSafekeepingAccountCommand command)
    {
        command.RouteTenantId = tenantId;
        var id = await sender.Send(command);
        return TypedResults.Created($"/tenants/{tenantId}/safekeeping-accounts/{id}", new CreatedSafekeepingAccountDto(id));
    }

    [EndpointSummary("Disable a safekeeping account")]
    [EndpointDescription("Disables a safekeeping account. Idempotent.")]
    public static async Task<NoContent> DisableSafekeepingAccount(
        ISender sender, Guid tenantId, Guid id, DisableSafekeepingAccountCommand command)
    {
        command.TenantId = tenantId;
        command.Id = id;
        await sender.Send(command);
        return TypedResults.NoContent();
    }

    [EndpointSummary("Enable a safekeeping account")]
    [EndpointDescription("Enables a safekeeping account again. Idempotent.")]
    public static async Task<NoContent> EnableSafekeepingAccount(
        ISender sender, Guid tenantId, Guid id, EnableSafekeepingAccountCommand command)
    {
        command.TenantId = tenantId;
        command.Id = id;
        await sender.Send(command);
        return TypedResults.NoContent();
    }
}

/// <summary>Configured custodian connections (in-kind-transfers R7).</summary>
public class CustodianConnections : IEndpointGroup
{
    public static string RoutePrefix => "/custodian-connections";

    public static void Map(RouteGroupBuilder groupBuilder) =>
        groupBuilder.MapGet(GetCustodianConnections).RequireAuthorization(Policies.TenantsManage);

    [EndpointSummary("List custodian connections")]
    [EndpointDescription("Returns the configured custodian connections, name and adapter only. SystemAdmin only.")]
    public static async Task<Ok<PagedList<CustodianConnectionDto>>> GetCustodianConnections(ISender sender) =>
        TypedResults.Ok(await sender.Send(new GetCustodianConnectionsQuery()));
}
