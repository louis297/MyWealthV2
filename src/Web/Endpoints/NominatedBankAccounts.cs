using Microsoft.AspNetCore.Http.HttpResults;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.NominatedBankAccounts;
using MyWealthV2.Application.NominatedBankAccounts.Commands;
using MyWealthV2.Application.NominatedBankAccounts.Queries;

namespace MyWealthV2.Web.Endpoints;

/// <summary>A Customer's nominated bank accounts (docs/features/withdrawals.md §4.1, §8).</summary>
public class NominatedBankAccounts : IEndpointGroup
{
    public static string RoutePrefix => "/nominated-bank-accounts";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(GetNominatedBankAccounts).RequireAuthorization(Policies.NominatedBankAccountsRead);
        groupBuilder.MapGet(GetNominatedBankAccount, "{id}").RequireAuthorization(Policies.NominatedBankAccountsRead);
        groupBuilder.MapPost(AddNominatedBankAccount).RequireAuthorization(Policies.NominatedBankAccountsManage);
        groupBuilder.MapPost(VerifyNominatedBankAccount, "{id}/verify").RequireAuthorization(Policies.NominatedBankAccountsVerify);
        groupBuilder.MapPost(RejectNominatedBankAccount, "{id}/reject").RequireAuthorization(Policies.NominatedBankAccountsVerify);
        groupBuilder.MapPost(RemoveNominatedBankAccount, "{id}/remove").RequireAuthorization(Policies.NominatedBankAccountsManage);
    }

    [EndpointSummary("List nominated bank accounts")]
    [EndpointDescription("Returns the nominated bank accounts of the Customers in the caller's scope, newest added first (R13).")]
    public static async Task<Ok<PagedList<NominatedBankAccountDto>>> GetNominatedBankAccounts(
        ISender sender,
        int page = 1,
        int pageSize = 20,
        Guid? customerId = null,
        string? status = null,
        string? currency = null,
        Guid? tenantId = null) =>
        TypedResults.Ok(await sender.Send(
            new GetNominatedBankAccountsQuery(page, pageSize, customerId, status, currency, tenantId)));

    [EndpointSummary("Get a nominated bank account")]
    [EndpointDescription("Returns one nominated bank account in the caller's scope (R13).")]
    public static async Task<Ok<NominatedBankAccountDto>> GetNominatedBankAccount(ISender sender, Guid id) =>
        TypedResults.Ok(await sender.Send(new GetNominatedBankAccountByIdQuery(id)));

    [EndpointSummary("Add a nominated bank account")]
    [EndpointDescription("Adds a Customer's own bank account, Pending until a TenantAdmin other than the caller checks it (R4).")]
    public static async Task<Created<CreatedIdDto>> AddNominatedBankAccount(
        ISender sender,
        AddNominatedBankAccountCommand command)
    {
        var id = await sender.Send(command);
        return TypedResults.Created($"{RoutePrefix}/{id}", new CreatedIdDto(id));
    }

    [EndpointSummary("Verify a nominated bank account")]
    [EndpointDescription("A TenantAdmin other than the person who added it records what was checked (R7–R11).")]
    public static async Task<NoContent> VerifyNominatedBankAccount(
        ISender sender,
        Guid id,
        VerifyNominatedBankAccountCommand command)
    {
        command.Id = id;
        await sender.Send(command);
        return TypedResults.NoContent();
    }

    [EndpointSummary("Reject a nominated bank account")]
    [EndpointDescription("A TenantAdmin other than the person who added it refuses it, with a reason (R7–R11).")]
    public static async Task<NoContent> RejectNominatedBankAccount(
        ISender sender,
        Guid id,
        RejectNominatedBankAccountCommand command)
    {
        command.Id = id;
        await sender.Send(command);
        return TypedResults.NoContent();
    }

    [EndpointSummary("Remove a nominated bank account")]
    [EndpointDescription("Removes a Pending or Verified bank account, with a note. Withdrawals already approved to it are still paid (R7, R14).")]
    public static async Task<NoContent> RemoveNominatedBankAccount(
        ISender sender,
        Guid id,
        RemoveNominatedBankAccountCommand command)
    {
        command.Id = id;
        await sender.Send(command);
        return TypedResults.NoContent();
    }
}
