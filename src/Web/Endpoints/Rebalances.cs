using Microsoft.AspNetCore.Http.HttpResults;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Rebalances;
using MyWealthV2.Application.Rebalances.Commands;
using MyWealthV2.Application.Rebalances.Queries;

namespace MyWealthV2.Web.Endpoints;

/// <summary>Rebalances of assigned Custodial accounts (docs/features/rebalances.md §8).</summary>
public class Rebalances : IEndpointGroup
{
    public static string RoutePrefix => "/rebalances";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(GetRebalances).RequireAuthorization(Policies.RebalancesRead);
        groupBuilder.MapGet(GetRebalance, "{id:guid}").RequireAuthorization(Policies.RebalancesRead);
        groupBuilder.MapPost(ConfirmRebalance, "{id:guid}/confirm").RequireAuthorization(Policies.RebalancesConfirm);
    }

    [EndpointSummary("List rebalances")]
    [EndpointDescription("Returns the rebalances of the accounts in the caller's scope, newest proposed first, without their targets, lines and orders (R27).")]
    public static async Task<Ok<PagedList<RebalanceDto>>> GetRebalances(
        ISender sender,
        int page = 1,
        int pageSize = 20,
        Guid? accountId = null,
        Guid? customerId = null,
        Guid? portfolioId = null,
        string? status = null,
        bool? open = null,
        Guid? tenantId = null) =>
        TypedResults.Ok(await sender.Send(new GetRebalancesQuery(
            page, pageSize, accountId, customerId, portfolioId, status, open, tenantId)));

    [EndpointSummary("Get a rebalance")]
    [EndpointDescription("Returns one rebalance in the caller's scope with its own targets, the lines of its proposal and its orders, oldest first (R27).")]
    public static async Task<Ok<RebalanceDto>> GetRebalance(ISender sender, Guid id) =>
        TypedResults.Ok(await sender.Send(new GetRebalanceByIdQuery(id)));

    [EndpointSummary("Confirm a rebalance")]
    [EndpointDescription("Confirms a proposal that has not expired: the plan is made again against the rebalance's own targets and the first wave of orders is placed. Nothing is sent to the broker in the request (R13, R14).")]
    public static async Task<NoContent> ConfirmRebalance(ISender sender, Guid id, ConfirmRebalanceCommand command)
    {
        command.Id = id;
        await sender.Send(command);
        return TypedResults.NoContent();
    }
}
