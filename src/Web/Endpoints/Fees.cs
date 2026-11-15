using Microsoft.AspNetCore.Http.HttpResults;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Fees.TradeSchedules;
using MyWealthV2.Application.Fees.TradeSchedules.Commands;
using MyWealthV2.Application.Fees.TradeSchedules.Queries;

namespace MyWealthV2.Web.Endpoints;

/// <summary>
/// Trade fee schedules under the <c>/fees</c> namespace prefix (api-design §7.9). There is no <c>GET /fees</c>.
/// <c>{id}</c> takes a GUID only, so <c>preview</c> is never read as an id.
/// </summary>
public class Fees : IEndpointGroup
{
    public static string RoutePrefix => "/fees/trade-schedules";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(GetTradeFeeSchedules).RequireAuthorization(Policies.FeesRead);
        groupBuilder.MapGet(GetTradeFeeSchedule, "{id:guid}").RequireAuthorization(Policies.FeesRead);
        groupBuilder.MapPost(CreateTradeFeeSchedule).RequireAuthorization(Policies.FeesManage);
        groupBuilder.MapPost(PreviewTradeFees, "preview").RequireAuthorization(Policies.FeesRead);
        groupBuilder.MapPut(UpdateTradeFeeSchedule, "{id:guid}").RequireAuthorization(Policies.FeesManage);
        groupBuilder.MapDelete(DeleteTradeFeeSchedule, "{id:guid}").RequireAuthorization(Policies.FeesManage);
    }

    [EndpointSummary("List trade fee schedules")]
    [EndpointDescription("Returns a paged list of trade fee schedules in one tenant, by quote currency then effective date.")]
    public static async Task<Ok<PagedList<TradeFeeScheduleDto>>> GetTradeFeeSchedules(
        ISender sender,
        int page = 1,
        int pageSize = 20,
        string? quoteCurrency = null,
        Guid? tenantId = null)
    {
        var result = await sender.Send(new GetTradeFeeSchedulesQuery(page, pageSize, quoteCurrency, tenantId));
        return TypedResults.Ok(result);
    }

    [EndpointSummary("Get a trade fee schedule")]
    [EndpointDescription("Returns one trade fee schedule by PublicId, with whether it is in effect.")]
    public static async Task<Ok<TradeFeeScheduleDto>> GetTradeFeeSchedule(ISender sender, Guid id)
    {
        var item = await sender.Send(new GetTradeFeeScheduleByIdQuery(id));
        return TypedResults.Ok(item);
    }

    [EndpointSummary("Create a trade fee schedule")]
    [EndpointDescription("Adds a schedule row for one quote currency from a date after the cut-off date.")]
    public static async Task<Created<CreatedTradeFeeScheduleDto>> CreateTradeFeeSchedule(
        ISender sender,
        CreateTradeFeeScheduleCommand command)
    {
        var id = await sender.Send(command);
        return TypedResults.Created($"/fees/trade-schedules/{id}", new CreatedTradeFeeScheduleDto(id));
    }

    [EndpointSummary("Preview trade fees")]
    [EndpointDescription("Computes the trade fee for up to 5 amounts under a schedule that is not saved. Writes nothing; not a quote for any trade.")]
    public static async Task<Ok<TradeFeePreviewDto>> PreviewTradeFees(ISender sender, PreviewTradeFeesQuery query)
    {
        var preview = await sender.Send(query);
        return TypedResults.Ok(preview);
    }

    [EndpointSummary("Update a trade fee schedule")]
    [EndpointDescription("Replaces a pending schedule row. A row in effect cannot change. The quote currency cannot change.")]
    public static async Task<NoContent> UpdateTradeFeeSchedule(
        ISender sender,
        Guid id,
        UpdateTradeFeeScheduleCommand command)
    {
        command.Id = id;
        await sender.Send(command);
        return TypedResults.NoContent();
    }

    [EndpointSummary("Delete a trade fee schedule")]
    [EndpointDescription("Deletes a pending schedule row. A row in effect cannot be deleted.")]
    public static async Task<NoContent> DeleteTradeFeeSchedule(ISender sender, Guid id)
    {
        await sender.Send(new DeleteTradeFeeScheduleCommand(id));
        return TypedResults.NoContent();
    }
}

public sealed record CreatedTradeFeeScheduleDto(Guid Id);
