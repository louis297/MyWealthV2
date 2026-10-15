using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Reports;
using MyWealthV2.Application.Reports.Commands.RequestReport;
using MyWealthV2.Application.Reports.Queries.GetReports;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using MyWealthV2.Web.Infrastructure;

namespace MyWealthV2.Web.Endpoints;

public class Reports : IEndpointGroup
{
    public static string RoutePrefix => "/reports";

    private static readonly AuthorizationPolicy CreateReports = new AuthorizationPolicyBuilder()
        .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()
        .AddRequirements(new AnyPermissionRequirement(
            Policies.ReportsCustomerCreate,
            Policies.ReportsBookCreate))
        .Build();

    private static readonly AuthorizationPolicy ReadReports = new AuthorizationPolicyBuilder()
        .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()
        .AddRequirements(new AnyPermissionRequirement(
            Policies.ReportsCustomerRead,
            Policies.ReportsBookRead))
        .Build();

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(GetReports).RequireAuthorization(ReadReports);
        groupBuilder.MapPost(RequestReport, "request").RequireAuthorization(CreateReports);
    }

    [EndpointSummary("List granted reports")]
    [EndpointDescription("Returns active reports granted to the caller's tenant. Optional scope is Customer or Book.")]
    public static async Task<Ok<IReadOnlyList<ReportCatalogDto>>> GetReports(
        ISender sender,
        string? scope = null)
    {
        var result = await sender.Send(new GetReportsQuery(scope));
        return TypedResults.Ok(result);
    }

    [EndpointSummary("Request a report")]
    [EndpointDescription("Queues a manual report run. Returns the execution id.")]
    public static async Task<IResult> RequestReport(ISender sender, RequestReportCommand command)
    {
        var id = await sender.Send(command);
        return TypedResults.Accepted($"/reports/executions/{id}", new { id });
    }
}
