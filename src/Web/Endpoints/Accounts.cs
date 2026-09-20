using MyWealthV2.Application.Accounts;
using MyWealthV2.Application.Accounts.Commands.CloseAccount;
using MyWealthV2.Application.Accounts.Commands.CreateAccount;
using MyWealthV2.Application.Accounts.Commands.ReopenAccount;
using MyWealthV2.Application.Accounts.Commands.UpdateAccount;
using MyWealthV2.Application.Accounts.Queries.GetAccountById;
using MyWealthV2.Application.Accounts.Queries.GetAccounts;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MyWealthV2.Web.Endpoints;

public class Accounts : IEndpointGroup
{
    public static string RoutePrefix => "/accounts";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(GetAccounts).RequireAuthorization(Policies.AccountsRead);
        groupBuilder.MapGet(GetAccount, "{id}").RequireAuthorization(Policies.AccountsRead);
        groupBuilder.MapPost(CreateAccount).RequireAuthorization(Policies.AccountsCreate);
        groupBuilder.MapPut(UpdateAccount, "{id}").RequireAuthorization(Policies.AccountsManage);
        groupBuilder.MapPost(CloseAccount, "{id}/close").RequireAuthorization(Policies.AccountsManage);
        groupBuilder.MapPost(ReopenAccount, "{id}/reopen").RequireAuthorization(Policies.AccountsManage);
    }

    [EndpointSummary("List accounts")]
    [EndpointDescription("Returns a paged list of accounts in one tenant.")]
    public static async Task<Ok<PagedList<AccountDto>>> GetAccounts(
        ISender sender,
        int page = 1,
        int pageSize = 20,
        string? enabledOnly = null,
        string? search = null,
        Guid? tenantId = null,
        Guid? customerId = null,
        string? type = null)
    {
        var result = await sender.Send(
            new GetAccountsQuery(page, pageSize, enabledOnly, search, tenantId, customerId, type));
        return TypedResults.Ok(result);
    }

    [EndpointSummary("Get an account")]
    [EndpointDescription("Returns one account by PublicId.")]
    public static async Task<Ok<AccountDto>> GetAccount(ISender sender, Guid id)
    {
        var item = await sender.Send(new GetAccountByIdQuery(id));
        return TypedResults.Ok(item);
    }

    [EndpointSummary("Create an account")]
    [EndpointDescription("Opens an account under a Customer in the current tenant, or in a chosen tenant for SystemAdmin.")]
    public static async Task<Created<CreatedAccountDto>> CreateAccount(
        ISender sender,
        CreateAccountCommand command)
    {
        var id = await sender.Send(command);
        return TypedResults.Created($"/accounts/{id}", new CreatedAccountDto(id));
    }

    [EndpointSummary("Rename an account")]
    [EndpointDescription("Updates the account display name. Type and currency cannot change.")]
    public static async Task<NoContent> UpdateAccount(ISender sender, Guid id, UpdateAccountCommand command)
    {
        command.Id = id;
        await sender.Send(command);
        return TypedResults.NoContent();
    }

    [EndpointSummary("Close an account")]
    [EndpointDescription("Closes an account. Idempotent.")]
    public static async Task<NoContent> CloseAccount(ISender sender, Guid id, CloseAccountCommand command)
    {
        command.Id = id;
        await sender.Send(command);
        return TypedResults.NoContent();
    }

    [EndpointSummary("Reopen an account")]
    [EndpointDescription("Reopens an account. Idempotent.")]
    public static async Task<NoContent> ReopenAccount(ISender sender, Guid id, ReopenAccountCommand command)
    {
        command.Id = id;
        await sender.Send(command);
        return TypedResults.NoContent();
    }
}
