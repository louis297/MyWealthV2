using Microsoft.AspNetCore.Http.HttpResults;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Interest.Runs;

namespace MyWealthV2.Web.Endpoints;

/// <summary>Interest runs under the <c>/interest</c> namespace prefix (api-design §7.10). <c>{id}</c> takes a GUID only.</summary>
public class InterestRuns : IEndpointGroup
{
    public static string RoutePrefix => "/interest/runs";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(GetInterestRuns).RequireAuthorization(Policies.InterestRunsRead);
        groupBuilder.MapGet(GetInterestRun, "{id:guid}").RequireAuthorization(Policies.InterestRunsRead);
        groupBuilder.MapPost(StartInterestRun).RequireAuthorization(Policies.InterestRunsStart);
    }

    [EndpointSummary("List interest runs")]
    [EndpointDescription("A Tenant's interest runs, newest first, with totals per currency. A SystemAdmin names the tenant.")]
    public static async Task<Ok<PagedList<InterestRunDto>>> GetInterestRuns(
        ISender sender,
        int page = 1,
        int pageSize = 20,
        Guid? tenantId = null,
        string? trigger = null,
        DateOnly? from = null,
        DateOnly? to = null) =>
        TypedResults.Ok(await sender.Send(new GetInterestRunsQuery(page, pageSize, tenantId, trigger, from, to)));

    [EndpointSummary("Get an interest run")]
    [EndpointDescription("One interest run with its posting date, counts and totals per currency.")]
    public static async Task<Ok<InterestRunDto>> GetInterestRun(ISender sender, Guid id) =>
        TypedResults.Ok(await sender.Send(new GetInterestRunByIdQuery(id)));

    [EndpointSummary("Start an interest run")]
    [EndpointDescription("Runs interest at once for a Tenant and an as-of date of at most yesterday (UTC), unless Interest:AllowFutureAsOfDates is on.")]
    public static async Task<Created<CreatedInterestRunDto>> StartInterestRun(ISender sender, StartInterestRunCommand command)
    {
        var id = await sender.Send(command);
        return TypedResults.Created($"/interest/runs/{id}", new CreatedInterestRunDto(id));
    }
}
