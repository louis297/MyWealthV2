using Microsoft.AspNetCore.Http.HttpResults;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Interest.RateSchedules;

namespace MyWealthV2.Web.Endpoints;

/// <summary>
/// Interest rate schedules under the <c>/interest</c> namespace prefix (api-design §7.10). There is no
/// <c>GET /interest</c>. <c>{id}</c> takes a GUID only.
/// </summary>
public class InterestRateSchedules : IEndpointGroup
{
    public static string RoutePrefix => "/interest/rate-schedules";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(GetInterestRateSchedules).RequireAuthorization(Policies.InterestRatesRead);
        groupBuilder.MapGet(GetInterestRateSchedule, "{id:guid}").RequireAuthorization(Policies.InterestRatesRead);
        groupBuilder.MapPost(CreateInterestRateSchedule).RequireAuthorization(Policies.InterestRatesManage);
        groupBuilder.MapPut(UpdateInterestRateSchedule, "{id:guid}").RequireAuthorization(Policies.InterestRatesManage);
        groupBuilder.MapDelete(DeleteInterestRateSchedule, "{id:guid}").RequireAuthorization(Policies.InterestRatesManage);
    }

    [EndpointSummary("List interest rate schedules")]
    [EndpointDescription("Returns the tenant's interest rate schedules by currency then effective date, with whether each is in effect.")]
    public static async Task<Ok<PagedList<InterestRateScheduleDto>>> GetInterestRateSchedules(
        ISender sender,
        int page = 1,
        int pageSize = 20,
        string? currency = null,
        string? tenantId = null) =>
        TypedResults.Ok(await sender.Send(new GetInterestRateSchedulesQuery(page, pageSize, currency, tenantId)));

    [EndpointSummary("Get an interest rate schedule")]
    [EndpointDescription("Returns one interest rate schedule by PublicId, with whether it is in effect.")]
    public static async Task<Ok<InterestRateScheduleDto>> GetInterestRateSchedule(ISender sender, Guid id) =>
        TypedResults.Ok(await sender.Send(new GetInterestRateScheduleQuery(id)));

    [EndpointSummary("Create an interest rate schedule")]
    [EndpointDescription("Adds a row of marginal annual rates for one currency from a date after the cut-off date.")]
    public static async Task<Created<CreatedInterestRateScheduleDto>> CreateInterestRateSchedule(
        ISender sender,
        CreateInterestRateScheduleCommand command)
    {
        var id = await sender.Send(command);
        return TypedResults.Created($"/interest/rate-schedules/{id}", new CreatedInterestRateScheduleDto(id));
    }

    [EndpointSummary("Update an interest rate schedule")]
    [EndpointDescription("Replaces the date and every band of a pending row. A row in effect is 409 interest_schedule_in_effect.")]
    public static async Task<NoContent> UpdateInterestRateSchedule(ISender sender, Guid id, UpdateInterestRateScheduleCommand command)
    {
        command.Id = id;
        await sender.Send(command);
        return TypedResults.NoContent();
    }

    [EndpointSummary("Delete an interest rate schedule")]
    [EndpointDescription("Deletes a pending row. A row in effect is 409 interest_schedule_in_effect.")]
    public static async Task<NoContent> DeleteInterestRateSchedule(ISender sender, Guid id)
    {
        await sender.Send(new DeleteInterestRateScheduleCommand(id));
        return TypedResults.NoContent();
    }
}
