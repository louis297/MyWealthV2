using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Withdrawals;
using MyWealthV2.Application.Withdrawals.Commands;
using MyWealthV2.Application.Withdrawals.Queries;

namespace MyWealthV2.Web.Endpoints;

/// <summary>Withdrawals from Custodial accounts to nominated bank accounts (docs/features/withdrawals.md §4.2, §8).</summary>
public class Withdrawals : IEndpointGroup
{
    public static string RoutePrefix => "/withdrawals";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(GetWithdrawals).RequireAuthorization(Policies.WithdrawalsRead);
        groupBuilder.MapGet(GetWithdrawal, "{id}").RequireAuthorization(Policies.WithdrawalsRead);
        groupBuilder.MapPost(RequestWithdrawal).RequireAuthorization(Policies.WithdrawalsRequest);
        groupBuilder.MapPost(ApproveWithdrawal, "{id}/approve").RequireAuthorization(Policies.WithdrawalsApprove);
        groupBuilder.MapPost(RejectWithdrawal, "{id}/reject").RequireAuthorization(Policies.WithdrawalsApprove);
        groupBuilder.MapPost(CancelWithdrawal, "{id}/cancel").RequireAuthorization(Policies.WithdrawalsRequest);
    }

    [EndpointSummary("List withdrawals")]
    [EndpointDescription("Returns the withdrawals of the accounts in the caller's scope, newest requested first (R26).")]
    public static async Task<Ok<PagedList<WithdrawalDto>>> GetWithdrawals(
        ISender sender,
        int page = 1,
        int pageSize = 20,
        Guid? accountId = null,
        Guid? customerId = null,
        string? status = null,
        Guid? tenantId = null) =>
        TypedResults.Ok(await sender.Send(new GetWithdrawalsQuery(page, pageSize, accountId, customerId, status, tenantId)));

    [EndpointSummary("Get a withdrawal")]
    [EndpointDescription("Returns one withdrawal in the caller's scope (R25).")]
    public static async Task<Ok<WithdrawalDto>> GetWithdrawal(ISender sender, Guid id) =>
        TypedResults.Ok(await sender.Send(new GetWithdrawalByIdQuery(id)));

    [EndpointSummary("Request a withdrawal")]
    [EndpointDescription("Requests a withdrawal from a Custodial account to a verified nominated bank account and reserves the amount. Requires Idempotency-Key (R18–R23).")]
    public static async Task<IResult> RequestWithdrawal(
        ISender sender,
        HttpRequest httpRequest,
        RequestWithdrawalCommand command)
    {
        var key = httpRequest.Headers["Idempotency-Key"].ToString();
        command.IdempotencyKey = string.IsNullOrWhiteSpace(key) ? null : key;
        command.RequestMethod = httpRequest.Method;
        command.RequestPath = httpRequest.Path.Value ?? RoutePrefix;
        var result = await sender.Send(command);
        if (result.StatusCode == StatusCodes.Status201Created)
        {
            using var body = JsonDocument.Parse(result.Body);
            httpRequest.HttpContext.Response.Headers.Location = $"{RoutePrefix}/{body.RootElement.GetProperty("id").GetGuid()}";
        }

        return Results.Content(result.Body, "application/json", statusCode: result.StatusCode);
    }

    [EndpointSummary("Approve a withdrawal")]
    [EndpointDescription("A TenantAdmin other than the requester approves it; an outgoing payment waits to be sent (R27–R29, R31).")]
    public static async Task<NoContent> ApproveWithdrawal(ISender sender, Guid id, ApproveWithdrawalCommand command)
    {
        command.Id = id;
        await sender.Send(command);
        return TypedResults.NoContent();
    }

    [EndpointSummary("Reject a withdrawal")]
    [EndpointDescription("A TenantAdmin other than the requester refuses it, with a note; the reservation ends (R27, R28).")]
    public static async Task<NoContent> RejectWithdrawal(ISender sender, Guid id, RejectWithdrawalCommand command)
    {
        command.Id = id;
        await sender.Send(command);
        return TypedResults.NoContent();
    }

    [EndpointSummary("Cancel a withdrawal")]
    [EndpointDescription("Withdraws a requested withdrawal, or an approved one the bank was not asked to pay yet (R27, R30).")]
    public static async Task<NoContent> CancelWithdrawal(ISender sender, Guid id, CancelWithdrawalCommand command)
    {
        command.Id = id;
        await sender.Send(command);
        return TypedResults.NoContent();
    }
}
