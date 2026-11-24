using Microsoft.AspNetCore.Authorization;
using MyWealthV2.Application.Accounts;
using MyWealthV2.Application.Accounts.Commands.CloseAccount;
using MyWealthV2.Application.Accounts.Commands.CreateAccount;
using MyWealthV2.Application.Accounts.Commands.ReopenAccount;
using MyWealthV2.Application.Accounts.Commands.SetDividendReinvestment;
using MyWealthV2.Application.Accounts.Commands.UpdateAccount;
using MyWealthV2.Application.Accounts.Queries.GetAccountById;
using MyWealthV2.Application.Accounts.Queries.GetAccountCashBalance;
using MyWealthV2.Application.Accounts.Queries.GetAccounts;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Interest.Accruals;
using MyWealthV2.Application.Common.Security;
using Microsoft.AspNetCore.Http.HttpResults;
using MyWealthV2.Application.Accounts.Queries.GetAccountHoldings;
using MyWealthV2.Application.Accounts.Queries.GetDepositInstructions;
using MyWealthV2.Application.Portfolios;
using MyWealthV2.Application.Portfolios.Commands.AssignPortfolio;
using MyWealthV2.Application.Portfolios.Commands.RebalanceAccount;
using MyWealthV2.Application.Portfolios.Commands.UnassignPortfolio;
using MyWealthV2.Application.Portfolios.Queries.GetAccountPortfolio;
using MyWealthV2.Web.Infrastructure;

using MyWealthV2.Application.Rebalances;

namespace MyWealthV2.Web.Endpoints;

public class Accounts : IEndpointGroup
{
    public static string RoutePrefix => "/accounts";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(GetAccounts).RequireAuthorization(Policies.AccountsRead);
        groupBuilder.MapGet(GetCashBalance, "{id}/cash-balance").RequireAuthorization(Policies.AccountsRead);
        groupBuilder.MapGet(GetAccount, "{id}").RequireAuthorization(Policies.AccountsRead);
        groupBuilder.MapPost(CreateAccount).RequireAuthorization(Policies.AccountsCreate);
        groupBuilder.MapPut(UpdateAccount, "{id}").RequireAuthorization(Policies.AccountsManage);
        groupBuilder.MapPut(SetDividendReinvestment, "{accountId:guid}/dividend-reinvestment").RequireAuthorization(Policies.AccountsManage);
        groupBuilder.MapPost(CloseAccount, "{id}/close").RequireAuthorization(Policies.AccountsManage);
        groupBuilder.MapPost(ReopenAccount, "{id}/reopen").RequireAuthorization(Policies.AccountsManage);
        groupBuilder.MapGet(GetHoldings, "{id}/holdings").RequireAuthorization(Policies.AccountsRead);
        groupBuilder.MapGet(GetAccountPortfolio, "{id}/portfolio").RequireAuthorization(ReadPortfolio);
        groupBuilder.MapGet(GetDepositInstructions, "{id}/deposit-instructions").RequireAuthorization(Policies.AccountsRead);
        groupBuilder.MapGet(GetInterestAccruals, "{id:guid}/interest-accruals").RequireAuthorization(Policies.AccountsRead);
        groupBuilder.MapPost(AssignPortfolio, "{id}/portfolio").RequireAuthorization(Policies.TransactionsCreate);
        groupBuilder.MapDelete(UnassignPortfolio, "{id}/portfolio").RequireAuthorization(Policies.TransactionsCreate);
        groupBuilder.MapPost(RebalanceAccount, "{id}/rebalance").RequireAuthorization(Policies.TransactionsCreate);
    }

    private static readonly AuthorizationPolicy ReadPortfolio = new AuthorizationPolicyBuilder()
        .AddRequirements(new AnyPermissionRequirement(Policies.PortfoliosRead, Policies.AccountsRead))
        .Build();

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
        string? type = null,
        string? custodyMode = null,
        string? depositReference = null)
    {
        var result = await sender.Send(
            new GetAccountsQuery(page, pageSize, enabledOnly, search, tenantId, customerId, type, custodyMode, depositReference));
        return TypedResults.Ok(result);
    }

    [EndpointSummary("Get an account cash balance")]
    [EndpointDescription("Returns the computed cash sum for one account.")]
    public static async Task<Ok<AccountCashBalanceDto>> GetCashBalance(ISender sender, Guid id)
    {
        var item = await sender.Send(new GetAccountCashBalanceQuery(id));
        return TypedResults.Ok(item);
    }

    [EndpointSummary("Get deposit instructions")]
    [EndpointDescription("Returns where to pay in to an Open Custodial account, and the reference to quote.")]
    public static async Task<Ok<DepositInstructionsDto>> GetDepositInstructions(ISender sender, Guid id)
    {
        var item = await sender.Send(new GetDepositInstructionsQuery(id));
        return TypedResults.Ok(item);
    }

    [EndpointSummary("List an account's daily interest accruals")]
    [EndpointDescription("The account's daily accrual rows, newest day first, with filters from, to, transactionId and posted.")]
    public static async Task<Ok<PagedList<InterestAccrualDto>>> GetInterestAccruals(
        ISender sender,
        Guid id,
        int page = 1,
        int pageSize = 20,
        DateOnly? from = null,
        DateOnly? to = null,
        Guid? transactionId = null,
        bool? posted = null) =>
        TypedResults.Ok(await sender.Send(new GetInterestAccrualsQuery(id, page, pageSize, from, to, transactionId, posted)));

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
    [EndpointDescription("Updates the account display name. Type, custody mode and currency cannot change.")]
    public static async Task<NoContent> UpdateAccount(ISender sender, Guid id, UpdateAccountCommand command)
    {
        command.Id = id;
        await sender.Send(command);
        return TypedResults.NoContent();
    }

    [EndpointSummary("Set the dividend reinvestment election")]
    [EndpointDescription("Sets whether a Custodial account reinvests dividends that offer a reinvestment plan.")]
    public static async Task<NoContent> SetDividendReinvestment(
        ISender sender,
        Guid accountId,
        SetAccountDividendReinvestmentCommand command)
    {
        command.AccountId = accountId;
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

    [EndpointSummary("Get all holdings")]
    [EndpointDescription("Get all holdings for an account")]
    public static async Task<Ok<AccountHoldingsDto>> GetHoldings(ISender sender, Guid id)
    {
        var result = await sender.Send(new GetAccountHoldingsQuery(id));
        return TypedResults.Ok(result);
    }

    [EndpointSummary("Get the account portfolio")]
    [EndpointDescription("Returns the active portfolio assignment and the last rebalance status.")]
    public static async Task<Ok<AccountPortfolioDto>> GetAccountPortfolio(ISender sender, Guid id)
    {
        var result = await sender.Send(new GetAccountPortfolioQuery(id));
        return TypedResults.Ok(result);
    }

    [EndpointSummary("Assign a portfolio")]
    [EndpointDescription("Assigns one portfolio to an open Custodial account and proposes a rebalance of it (rebalances R10).")]
    public static async Task<Ok<RebalanceProposalDto>> AssignPortfolio(
        ISender sender,
        Guid id,
        AssignPortfolioCommand command)
    {
        command.AccountId = id;
        var result = await sender.Send(command);
        return TypedResults.Ok(result);
    }

    [EndpointSummary("Unassign a portfolio")]
    [EndpointDescription("Removes the active portfolio assignment and leaves holdings unchanged.")]
    public static async Task<NoContent> UnassignPortfolio(ISender sender, Guid id)
    {
        await sender.Send(new UnassignPortfolioCommand { AccountId = id });
        return TypedResults.NoContent();
    }

    [EndpointSummary("Propose a rebalance of an account")]
    [EndpointDescription("Proposes a rebalance of the account towards its portfolio's targets. Nothing is traded until a person confirms it at POST /rebalances/{id}/confirm (rebalances R10).")]
    public static async Task<Ok<RebalanceProposalDto>> RebalanceAccount(ISender sender, Guid id) =>
        TypedResults.Ok(await sender.Send(new RebalanceAccountCommand { AccountId = id }));
}
