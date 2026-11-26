using Microsoft.AspNetCore.Http.HttpResults;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Valuations.Runs;

namespace MyWealthV2.Web.Endpoints;

/// <summary>Valuation runs (api-design §7.18). <c>{id}</c> takes a GUID only.</summary>
public class ValuationRuns : IEndpointGroup
{
    public static string RoutePrefix => "/valuation-runs";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(GetValuationRuns).RequireAuthorization(Policies.ValuationRunsRead);
        groupBuilder.MapGet(GetValuationRun, "{id:guid}").RequireAuthorization(Policies.ValuationRunsRead);
        groupBuilder.MapPost(StartValuationRun).RequireAuthorization(Policies.ValuationRunsStart);
    }

    [EndpointSummary("List valuation runs")]
    [EndpointDescription("A Tenant's valuation runs, newest first, with their counts. A SystemAdmin names the tenant.")]
    public static async Task<Ok<PagedList<ValuationRunDto>>> GetValuationRuns(
        ISender sender,
        int page = 1,
        int pageSize = 20,
        Guid? tenantId = null,
        string? trigger = null,
        string? from = null,
        string? to = null) =>
        TypedResults.Ok(await sender.Send(new GetValuationRunsQuery(page, pageSize, tenantId, trigger, from, to)));

    [EndpointSummary("Get a valuation run")]
    [EndpointDescription("One valuation run with its through date, trigger and counts.")]
    public static async Task<Ok<ValuationRunDto>> GetValuationRun(ISender sender, Guid id) =>
        TypedResults.Ok(await sender.Send(new GetValuationRunByIdQuery(id)));

    [EndpointSummary("Start a valuation run")]
    [EndpointDescription("Values and stores every Custodial account of a Tenant at once, through a date no later than the last final day (today minus two days, UTC).")]
    public static async Task<Created<CreatedValuationRunDto>> StartValuationRun(ISender sender, StartValuationRunCommand command)
    {
        var id = await sender.Send(command);
        return TypedResults.Created($"/valuation-runs/{id}", new CreatedValuationRunDto(id));
    }
}
