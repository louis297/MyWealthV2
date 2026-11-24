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
}
