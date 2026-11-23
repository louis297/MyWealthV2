using Microsoft.AspNetCore.Http.HttpResults;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.TradeSettlements.Commands;

namespace MyWealthV2.Web.Endpoints;

/// <summary>The trade settlements of the caller's Tenant (docs/features/trade-settlement.md §4.4, §8).</summary>
public class TradeSettlements : IEndpointGroup
{
    public static string RoutePrefix => "/trade-settlements";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapPost(PayTradeSettlement, "{id:guid}/pay").RequireAuthorization(Policies.TradeSettlementsManage);
    }

    [EndpointSummary("Pay a trade settlement again")]
    [EndpointDescription("Pays the broker again, with a new instruction id, a settlement whose payment the bank refused or returned (R14).")]
    public static async Task<NoContent> PayTradeSettlement(ISender sender, Guid id, PayTradeSettlementCommand command)
    {
        command.Id = id;
        await sender.Send(command);
        return TypedResults.NoContent();
    }
}
