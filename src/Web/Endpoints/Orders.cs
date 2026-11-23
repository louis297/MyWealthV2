using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Orders;
using MyWealthV2.Application.Orders.Commands;
using MyWealthV2.Application.Orders.Queries;

namespace MyWealthV2.Web.Endpoints;

/// <summary>Orders on Custodial accounts (docs/features/orders.md §4.3, §8).</summary>
public class Orders : IEndpointGroup
{
    public static string RoutePrefix => "/orders";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(GetOrders).RequireAuthorization(Policies.OrdersRead);
        groupBuilder.MapGet(GetOrder, "{id:guid}").RequireAuthorization(Policies.OrdersRead);
        groupBuilder.MapPost(PlaceOrder).RequireAuthorization(Policies.OrdersPlace);
        groupBuilder.MapPost(PreviewOrder, "preview").RequireAuthorization(Policies.OrdersPlace);
        groupBuilder.MapPost(CancelOrder, "{id:guid}/cancel").RequireAuthorization(Policies.OrdersPlace);
        groupBuilder.MapPost(ResolveBrokenFill, "{id:guid}/fills/{fillId:guid}/resolve-break")
            .RequireAuthorization(Policies.TransactionsAdjust);
    }

    [EndpointSummary("List orders")]
    [EndpointDescription("Returns the orders of the accounts in the caller's scope, newest placed first, without their fills (R16).")]
    public static async Task<Ok<PagedList<OrderDto>>> GetOrders(
        ISender sender,
        int page = 1,
        int pageSize = 20,
        Guid? accountId = null,
        Guid? customerId = null,
        Guid? instrumentId = null,
        string? side = null,
        string? status = null,
        bool? pendingFill = null,
        Guid? tenantId = null,
        Guid? rebalanceId = null) =>
        TypedResults.Ok(await sender.Send(new GetOrdersQuery(
            page, pageSize, accountId, customerId, instrumentId, side, status, pendingFill, tenantId, rebalanceId)));

    [EndpointSummary("Get an order")]
    [EndpointDescription("Returns one order in the caller's scope with its fills (R15).")]
    public static async Task<Ok<OrderDto>> GetOrder(ISender sender, Guid id) =>
        TypedResults.Ok(await sender.Send(new GetOrderByIdQuery(id)));

    [EndpointSummary("Preview an order")]
    [EndpointDescription("Returns about what an order would cost, the most it can cost and what it would reserve, with the refusals of placing it. Writes nothing (R17).")]
    public static async Task<Ok<OrderPreviewDto>> PreviewOrder(ISender sender, PreviewOrderQuery query) =>
        TypedResults.Ok(await sender.Send(query));

    [EndpointSummary("Place an order")]
    [EndpointDescription("Places a market day order on a Custodial account and reserves the most it can cost (a buy) or its quantity (a sell). Nothing is sent to the broker in the request. Requires Idempotency-Key (R6–R13).")]
    public static async Task<IResult> PlaceOrder(ISender sender, HttpRequest httpRequest, PlaceOrderCommand command)
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

    [EndpointSummary("Cancel an order")]
    [EndpointDescription("Cancels an order that was not sent yet, or asks the broker to cancel one it may hold; a fill that arrives before the broker cancels is still booked (R18).")]
    public static async Task<NoContent> CancelOrder(ISender sender, Guid id, CancelOrderCommand command)
    {
        command.Id = id;
        await sender.Send(command);
        return TypedResults.NoContent();
    }

    [EndpointSummary("Resolve a broken trade by hand")]
    [EndpointDescription("Marks as resolved a fill the broker broke that the platform could not reverse, once the account was corrected with an adjustment. Writes nothing to the ledger (trade-settlement R31).")]
    public static async Task<NoContent> ResolveBrokenFill(ISender sender, Guid id, Guid fillId, ResolveBrokenFillCommand command)
    {
        command.Id = id;
        command.FillId = fillId;
        await sender.Send(command);
        return TypedResults.NoContent();
    }
}
