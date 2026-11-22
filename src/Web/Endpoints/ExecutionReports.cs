using Microsoft.AspNetCore.Http.HttpResults;
using MyWealthV2.Application.Broker.Queries;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Web.Endpoints;

/// <summary>The stored execution reports, platform data (docs/features/orders.md R30).</summary>
public class ExecutionReports : IEndpointGroup
{
    public static string RoutePrefix => "/execution-reports";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(GetExecutionReports).RequireAuthorization(Policies.TenantsManage);
    }

    [EndpointSummary("List execution reports")]
    [EndpointDescription("Returns the execution reports read from the broker connections, newest first, with how each was processed. Filters: connection, status (comma-separated). SystemAdmin only.")]
    public static async Task<Ok<PagedList<ExecutionReportDto>>> GetExecutionReports(
        ISender sender,
        int page = 1,
        int pageSize = 20,
        string? connection = null,
        string? status = null) =>
        TypedResults.Ok(await sender.Send(new GetExecutionReportsQuery(page, pageSize, connection, status)));
}
