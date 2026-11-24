using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.InKindTransfers;

namespace MyWealthV2.Web.Endpoints;

/// <summary>In-kind transfers of securities in and out of Custodial accounts (docs/features/in-kind-transfers.md §4.5, §8).</summary>
public class InKindTransfers : IEndpointGroup
{
    public static string RoutePrefix => "/in-kind-transfers";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(GetInKindTransfers).RequireAuthorization(Policies.InKindTransfersRead);
        groupBuilder.MapGet(GetInKindTransfer, "{id}").RequireAuthorization(Policies.InKindTransfersRead);
        groupBuilder.MapPost(RequestInKindTransfer).RequireAuthorization(Policies.InKindTransfersRequest);
        groupBuilder.MapPost(ApproveInKindTransfer, "{id}/approve").RequireAuthorization(Policies.InKindTransfersApprove);
        groupBuilder.MapPost(RejectInKindTransfer, "{id}/reject").RequireAuthorization(Policies.InKindTransfersApprove);
        groupBuilder.MapPost(CancelInKindTransfer, "{id}/cancel").RequireAuthorization(Policies.InKindTransfersRequest);
        groupBuilder.MapPost(SetAsideInKindTransfer, "{id}/set-aside").RequireAuthorization(Policies.InKindTransfersApprove);
    }

    [EndpointSummary("List in-kind transfers")]
    [EndpointDescription("Returns the in-kind transfers of the accounts in the caller's scope, newest requested first (R29).")]
    public static async Task<Ok<PagedList<InKindTransferDto>>> GetInKindTransfers(
        ISender sender,
        int page = 1,
        int pageSize = 20,
        Guid? accountId = null,
        Guid? customerId = null,
        Guid? instrumentId = null,
        string? direction = null,
        string? status = null,
        Guid? tenantId = null) =>
        TypedResults.Ok(await sender.Send(new GetInKindTransfersQuery(page, pageSize, accountId, customerId, instrumentId, direction, status, tenantId)));

    [EndpointSummary("Get an in-kind transfer")]
    [EndpointDescription("Returns one in-kind transfer in the caller's scope (R28).")]
    public static async Task<Ok<InKindTransferDto>> GetInKindTransfer(ISender sender, Guid id) =>
        TypedResults.Ok(await sender.Send(new GetInKindTransferByIdQuery(id)));

    [EndpointSummary("Request an in-kind transfer")]
    [EndpointDescription("Requests a transfer of securities in or out of a Custodial account. Requires Idempotency-Key (R21–R26).")]
    public static async Task<IResult> RequestInKindTransfer(ISender sender, HttpRequest httpRequest, RequestInKindTransferCommand command)
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

    [EndpointSummary("Approve an in-kind transfer")]
    [EndpointDescription("A TenantAdmin other than the requester approves it; the worker sends it to the custodian (R31).")]
    public static Task<NoContent> ApproveInKindTransfer(ISender sender, Guid id, ApproveInKindTransferCommand command) =>
        Act(sender, id, command);

    [EndpointSummary("Reject an in-kind transfer")]
    [EndpointDescription("A TenantAdmin other than the requester refuses it, with a note (R31).")]
    public static Task<NoContent> RejectInKindTransfer(ISender sender, Guid id, RejectInKindTransferCommand command) =>
        Act(sender, id, command);

    [EndpointSummary("Cancel an in-kind transfer")]
    [EndpointDescription("Withdraws a transfer before the custodian was asked to make it (R32).")]
    public static Task<NoContent> CancelInKindTransfer(ISender sender, Guid id, CancelInKindTransferCommand command) =>
        Act(sender, id, command);

    [EndpointSummary("Set an in-kind transfer aside")]
    [EndpointDescription("Ends a transfer the custodian was asked to make, with a note; books nothing (R33).")]
    public static Task<NoContent> SetAsideInKindTransfer(ISender sender, Guid id, SetAsideInKindTransferCommand command) =>
        Act(sender, id, command);

    private static async Task<NoContent> Act(ISender sender, Guid id, InKindTransferActionCommand command)
    {
        command.Id = id;
        // The runtime type picks the handler.
        await sender.Send((object)command);
        return TypedResults.NoContent();
    }
}
