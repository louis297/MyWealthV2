using Microsoft.AspNetCore.Http.HttpResults;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.TradeSettlements;
using MyWealthV2.Application.TradeSettlements.Commands;
using MyWealthV2.Application.TradeSettlements.Queries;

namespace MyWealthV2.Web.Endpoints;

/// <summary>The trade settlements of the caller's Tenant (docs/features/trade-settlement.md §4.4, §8).</summary>
public class TradeSettlements : IEndpointGroup
{
    public static string RoutePrefix => "/trade-settlements";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(GetTradeSettlements).RequireAuthorization(Policies.TradeSettlementsManage);
        groupBuilder.MapGet(GetTradeSettlement, "{id:guid}").RequireAuthorization(Policies.TradeSettlementsManage);
        groupBuilder.MapPost(PayTradeSettlement, "{id:guid}/pay").RequireAuthorization(Policies.TradeSettlementsManage);
        groupBuilder.MapPost(SetAsideTradeSettlement, "{id:guid}/set-aside").RequireAuthorization(Policies.TradeSettlementsManage);
    }

    [EndpointSummary("List trade settlements")]
    [EndpointDescription("Returns the trade settlements of the caller's Tenant, newest first, without fills or payments. Filters: status (comma-separated), clientMoneyAccountId, open (R16).")]
    public static async Task<Ok<PagedList<TradeSettlementDto>>> GetTradeSettlements(
        ISender sender,
        int page = 1,
        int pageSize = 20,
        string? status = null,
        Guid? clientMoneyAccountId = null,
        bool? open = null,
        Guid? tenantId = null) =>
        TypedResults.Ok(await sender.Send(new GetTradeSettlementsQuery(page, pageSize, status, clientMoneyAccountId, open, tenantId)));

    [EndpointSummary("Get a trade settlement")]
    [EndpointDescription("Returns one trade settlement with the platform's and the broker's figures, its fills and its payments (R16).")]
    public static async Task<Ok<TradeSettlementDto>> GetTradeSettlement(ISender sender, Guid id) =>
        TypedResults.Ok(await sender.Send(new GetTradeSettlementByIdQuery(id)));

    [EndpointSummary("Pay a trade settlement again")]
    [EndpointDescription("Pays the broker again, with a new instruction id, a settlement whose payment the bank refused or returned (R14).")]
    public static async Task<NoContent> PayTradeSettlement(ISender sender, Guid id, PayTradeSettlementCommand command)
    {
        command.Id = id;
        await sender.Send(command);
        return TypedResults.NoContent();
    }

    [EndpointSummary("Set a trade settlement aside")]
    [EndpointDescription("Ends a settlement the firm and the broker resolve outside the platform, with a required note. Its trades are then shown in the bank reconciliation as not settled through the trust account (R15).")]
    public static async Task<NoContent> SetAsideTradeSettlement(ISender sender, Guid id, SetAsideTradeSettlementCommand command)
    {
        command.Id = id;
        await sender.Send(command);
        return TypedResults.NoContent();
    }
}
