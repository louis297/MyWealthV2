using MyWealthV2.Application.ClientMoneyAccounts;
using MyWealthV2.Application.ClientMoneyAccounts.Commands.CreateClientMoneyAccount;
using MyWealthV2.Application.ClientMoneyAccounts.Commands.DisableClientMoneyAccount;
using MyWealthV2.Application.ClientMoneyAccounts.Commands.EnableClientMoneyAccount;
using MyWealthV2.Application.ClientMoneyAccounts.Queries.GetClientMoneyAccountById;
using MyWealthV2.Application.ClientMoneyAccounts.Queries.GetClientMoneyAccounts;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MyWealthV2.Web.Endpoints;

/// <summary>Client money accounts of one tenant (docs/features/client-money-accounts.md §8).</summary>
public class ClientMoneyAccounts : IEndpointGroup
{
    public static string RoutePrefix => "/tenants/{tenantId}/client-money-accounts";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(GetClientMoneyAccounts).RequireAuthorization(Policies.TenantsManage);
        groupBuilder.MapGet(GetClientMoneyAccount, "{id}").RequireAuthorization(Policies.TenantsManage);
        groupBuilder.MapPost(CreateClientMoneyAccount).RequireAuthorization(Policies.TenantsManage);
        groupBuilder.MapPost(DisableClientMoneyAccount, "{id}/disable").RequireAuthorization(Policies.TenantsManage);
        groupBuilder.MapPost(EnableClientMoneyAccount, "{id}/enable").RequireAuthorization(Policies.TenantsManage);
    }

    [EndpointSummary("List client money accounts")]
    [EndpointDescription("Returns a paged list of one tenant's client money accounts. SystemAdmin only.")]
    public static async Task<Ok<PagedList<ClientMoneyAccountDto>>> GetClientMoneyAccounts(
        ISender sender,
        Guid tenantId,
        int page = 1,
        int pageSize = 20,
        string? enabledOnly = null)
    {
        var result = await sender.Send(new GetClientMoneyAccountsQuery(tenantId, page, pageSize, enabledOnly));
        return TypedResults.Ok(result);
    }

    [EndpointSummary("Get a client money account")]
    [EndpointDescription("Returns one client money account of the tenant by PublicId. SystemAdmin only.")]
    public static async Task<Ok<ClientMoneyAccountDto>> GetClientMoneyAccount(ISender sender, Guid tenantId, Guid id)
    {
        var item = await sender.Send(new GetClientMoneyAccountByIdQuery(tenantId, id));
        return TypedResults.Ok(item);
    }

    [EndpointSummary("Create a client money account")]
    [EndpointDescription("Records the tenant's trust bank account for one currency. SystemAdmin only.")]
    public static async Task<Created<CreatedClientMoneyAccountDto>> CreateClientMoneyAccount(
        ISender sender,
        Guid tenantId,
        CreateClientMoneyAccountCommand command)
    {
        command.RouteTenantId = tenantId;
        var id = await sender.Send(command);
        return TypedResults.Created(
            $"/tenants/{tenantId}/client-money-accounts/{id}", new CreatedClientMoneyAccountDto(id));
    }

    [EndpointSummary("Disable a client money account")]
    [EndpointDescription("Disables a client money account. Idempotent.")]
    public static async Task<NoContent> DisableClientMoneyAccount(
        ISender sender,
        Guid tenantId,
        Guid id,
        DisableClientMoneyAccountCommand command)
    {
        command.TenantId = tenantId;
        command.Id = id;
        await sender.Send(command);
        return TypedResults.NoContent();
    }

    [EndpointSummary("Enable a client money account")]
    [EndpointDescription("Enables a client money account again. Idempotent.")]
    public static async Task<NoContent> EnableClientMoneyAccount(
        ISender sender,
        Guid tenantId,
        Guid id,
        EnableClientMoneyAccountCommand command)
    {
        command.TenantId = tenantId;
        command.Id = id;
        await sender.Send(command);
        return TypedResults.NoContent();
    }
}
