using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using MyWealthV2.Application.AdjustmentRequests;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Web.Endpoints;

/// <summary>Adjustment requests: a second person approves every adjustment (docs/features/adjustment-review.md §8).</summary>
public class AdjustmentRequests : IEndpointGroup
{
    public static string RoutePrefix => "/adjustment-requests";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(GetAdjustmentRequests).RequireAuthorization(Policies.TransactionsAdjust);
        groupBuilder.MapGet(GetAdjustmentRequest, "{id}").RequireAuthorization(Policies.TransactionsAdjust);
        groupBuilder.MapPost(RequestAdjustment).RequireAuthorization(Policies.TransactionsAdjust);
        groupBuilder.MapPost(ApproveAdjustmentRequest, "{id}/approve").RequireAuthorization(Policies.TransactionsAdjust);
        groupBuilder.MapPost(RejectAdjustmentRequest, "{id}/reject").RequireAuthorization(Policies.TransactionsAdjust);
        groupBuilder.MapPost(CancelAdjustmentRequest, "{id}/cancel").RequireAuthorization(Policies.TransactionsAdjust);
    }

    [EndpointSummary("List adjustment requests")]
    [EndpointDescription("Returns the tenant's adjustment requests, newest requested first (R20).")]
    public static async Task<Ok<PagedList<AdjustmentRequestDto>>> GetAdjustmentRequests(
        ISender sender,
        int page = 1,
        int pageSize = 20,
        string? status = null,
        Guid? accountId = null,
        Guid? customerId = null,
        Guid? tenantId = null) =>
        TypedResults.Ok(await sender.Send(new GetAdjustmentRequestsQuery(page, pageSize, status, accountId, customerId, tenantId)));

    [EndpointSummary("Get an adjustment request")]
    [EndpointDescription("Returns one adjustment request of the tenant (R19).")]
    public static async Task<Ok<AdjustmentRequestDto>> GetAdjustmentRequest(ISender sender, Guid id) =>
        TypedResults.Ok(await sender.Send(new GetAdjustmentRequestByIdQuery(id)));

    [EndpointSummary("Request an adjustment")]
    [EndpointDescription("Stores an adjustment for a second person to approve; posts nothing. Requires Idempotency-Key (R3, R4).")]
    public static async Task<IResult> RequestAdjustment(ISender sender, HttpRequest httpRequest, RequestAdjustmentCommand command)
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

    [EndpointSummary("Approve an adjustment request")]
    [EndpointDescription("A TenantAdmin other than the requester approves it, and the Adjustment is posted (R8, R9).")]
    public static Task<NoContent> ApproveAdjustmentRequest(ISender sender, Guid id, ApproveAdjustmentRequestCommand command) =>
        Act(sender, id, command);

    [EndpointSummary("Reject an adjustment request")]
    [EndpointDescription("A TenantAdmin other than the requester refuses it, with a note; nothing is posted (R8, R10).")]
    public static Task<NoContent> RejectAdjustmentRequest(ISender sender, Guid id, RejectAdjustmentRequestCommand command) =>
        Act(sender, id, command);

    [EndpointSummary("Cancel an adjustment request")]
    [EndpointDescription("Withdraws a request; nothing is posted (R11).")]
    public static Task<NoContent> CancelAdjustmentRequest(ISender sender, Guid id, CancelAdjustmentRequestCommand command) =>
        Act(sender, id, command);

    private static async Task<NoContent> Act(ISender sender, Guid id, AdjustmentRequestActionCommand command)
    {
        command.Id = id;
        // The runtime type picks the handler.
        await sender.Send((object)command);
        return TypedResults.NoContent();
    }
}
