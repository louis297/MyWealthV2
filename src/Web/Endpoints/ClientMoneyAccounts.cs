using MyWealthV2.Application.ClientMoneyAccounts;
using MyWealthV2.Application.ClientMoneyAccounts.Commands.CreateClientMoneyAccount;
using MyWealthV2.Application.ClientMoneyAccounts.Commands.DisableClientMoneyAccount;
using MyWealthV2.Application.ClientMoneyAccounts.Commands.EnableClientMoneyAccount;
using MyWealthV2.Application.ClientMoneyAccounts.Queries.GetClientMoneyAccountById;
using MyWealthV2.Application.ClientMoneyAccounts.Queries.GetClientMoneyAccounts;
using MyWealthV2.Application.Common.Models;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MyWealthV2.Web.Endpoints;

/// <summary>Client money accounts of one tenant (docs/features/client-money-accounts.md §8).</summary>
public class ClientMoneyAccounts : IEndpointGroup
{
    public static string RoutePrefix => "/tenants/{tenantId}/client-money-accounts";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(GetClientMoneyAccounts);
        groupBuilder.MapGet(GetClientMoneyAccount, "{id}");
        groupBuilder.MapPost(CreateClientMoneyAccount);
        groupBuilder.MapPost(DisableClientMoneyAccount, "{id}/disable");
        groupBuilder.MapPost(EnableClientMoneyAccount, "{id}/enable");
    }

    [EndpointSummary("List client money accounts")]
    [EndpointDescription("Returns a paged list of one tenant's client money accounts. SystemAdmin only.")]
    public static Task<Ok<PagedList<ClientMoneyAccountDto>>> GetClientMoneyAccounts(
        ISender sender,
        Guid tenantId,
        int page = 1,
        int pageSize = 20,
        string? enabledOnly = null) =>
        throw new NotImplementedException();

    [EndpointSummary("Get a client money account")]
    [EndpointDescription("Returns one client money account of the tenant by PublicId. SystemAdmin only.")]
    public static Task<Ok<ClientMoneyAccountDto>> GetClientMoneyAccount(ISender sender, Guid tenantId, Guid id) =>
        throw new NotImplementedException();

    [EndpointSummary("Create a client money account")]
    [EndpointDescription("Records the tenant's trust bank account for one currency. SystemAdmin only.")]
    public static Task<Created<CreatedClientMoneyAccountDto>> CreateClientMoneyAccount(
        ISender sender,
        Guid tenantId,
        CreateClientMoneyAccountCommand command) =>
        throw new NotImplementedException();

    [EndpointSummary("Disable a client money account")]
    [EndpointDescription("Disables a client money account. Idempotent.")]
    public static Task<NoContent> DisableClientMoneyAccount(
        ISender sender,
        Guid tenantId,
        Guid id,
        DisableClientMoneyAccountCommand command) =>
        throw new NotImplementedException();

    [EndpointSummary("Enable a client money account")]
    [EndpointDescription("Enables a client money account again. Idempotent.")]
    public static Task<NoContent> EnableClientMoneyAccount(
        ISender sender,
        Guid tenantId,
        Guid id,
        EnableClientMoneyAccountCommand command) =>
        throw new NotImplementedException();
}
