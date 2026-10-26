using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Reports;
using MyWealthV2.Application.Reports.Commands.CreateReportSchedule;
using MyWealthV2.Application.Reports.Commands.DisableReportSchedule;
using MyWealthV2.Application.Reports.Commands.RequestReport;
using MyWealthV2.Application.Reports.Commands.UpdateReportCatalog;
using MyWealthV2.Application.Reports.Commands.UpdateReportSchedule;
using MyWealthV2.Application.Reports.Queries.DownloadReportResult;
using MyWealthV2.Application.Reports.Queries.GetReportExecutions;
using MyWealthV2.Application.Reports.Queries.GetReportSchedules;
using MyWealthV2.Application.Reports.Queries.GetReportCatalog;
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
        groupBuilder.MapGet(GetCatalog, "catalog").RequireAuthorization(Policies.TenantsManage);
        groupBuilder.MapPut(UpdateCatalog, "{id:guid}").RequireAuthorization(Policies.TenantsManage);
        groupBuilder.MapGet(GetReports).RequireAuthorization(ReadReports);
        groupBuilder.MapGet(GetSchedules, "schedules").RequireAuthorization(ReadReports);
        groupBuilder.MapPost(CreateSchedule, "schedules").RequireAuthorization(CreateReports);
        groupBuilder.MapPut(UpdateSchedule, "schedules/{id}").RequireAuthorization(CreateReports);
        groupBuilder.MapPost(DisableSchedule, "schedules/{id}/disable").RequireAuthorization(CreateReports);
        groupBuilder.MapPost(RequestReport, "request").RequireAuthorization(CreateReports);
        groupBuilder.MapGet(GetExecutions, "executions").RequireAuthorization(ReadReports);
        groupBuilder.MapGet(DownloadReport, "executions/{id}/download").RequireAuthorization(ReadReports);
    }

    [EndpointSummary("List the platform report catalog")]
    [EndpointDescription("Returns every catalog row, including inactive, ordered by code. No grant field. SystemAdmin only.")]
    public static async Task<Ok<IReadOnlyList<PlatformReportDto>>> GetCatalog(ISender sender)
    {
        var result = await sender.Send(new GetReportCatalogQuery());
        return TypedResults.Ok(result);
    }

    [EndpointSummary("Update a catalog report")]
    [EndpointDescription("Sets catalog IsActive and/or AllowedTriggers. IsActive false revokes every tenant grant and removes Hangfire jobs. Schedule and execution rows stay. SystemAdmin only.")]
    public static async Task<NoContent> UpdateCatalog(ISender sender, Guid id, UpdateReportCatalogCommand command)
    {
        command.Id = id;
        await sender.Send(command);
        return TypedResults.NoContent();
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

    [EndpointSummary("List report schedules")]
    [EndpointDescription("Returns the caller's tenant schedules for reports they can read. Optional scope is Customer or Book.")]
    public static async Task<Ok<IReadOnlyList<ReportScheduleDto>>> GetSchedules(
        ISender sender,
        string? scope = null)
    {
        var result = await sender.Send(new GetReportSchedulesQuery(scope));
        return TypedResults.Ok(result);
    }

    [EndpointSummary("Create a report schedule")]
    [EndpointDescription("Creates a recurring schedule for a granted report and registers its Hangfire job.")]
    public static async Task<IResult> CreateSchedule(ISender sender, CreateReportScheduleCommand command)
    {
        var id = await sender.Send(command);
        return TypedResults.Created($"/reports/schedules/{id}", new { id });
    }

    [EndpointSummary("Update a report schedule")]
    [EndpointDescription("Replaces a schedule's parameters, cron, and active flag, and updates its Hangfire job.")]
    public static async Task<NoContent> UpdateSchedule(
        ISender sender,
        Guid id,
        UpdateReportScheduleCommand command)
    {
        command.Id = id;
        await sender.Send(command);
        return TypedResults.NoContent();
    }

    [EndpointSummary("Disable a report schedule")]
    [EndpointDescription("Marks a schedule inactive and removes its Hangfire job. The row stays.")]
    public static async Task<NoContent> DisableSchedule(ISender sender, Guid id)
    {
        await sender.Send(new DisableReportScheduleCommand(id));
        return TypedResults.NoContent();
    }

    [EndpointSummary("Request a report")]
    [EndpointDescription("Queues a manual report run. Returns the execution id.")]
    public static async Task<IResult> RequestReport(ISender sender, RequestReportCommand command)
    {
        var id = await sender.Send(command);
        return TypedResults.Accepted($"/reports/executions/{id}", new { id });
    }

    [EndpointSummary("List report executions")]
    [EndpointDescription("Returns a paged list of report executions the caller can read. Optional scope is Customer or Book.")]
    public static async Task<Ok<PagedList<ReportExecutionDto>>> GetExecutions(
        ISender sender,
        int page = 1,
        int pageSize = 20,
        string? scope = null)
    {
        var result = await sender.Send(new GetReportExecutionsQuery(page, pageSize, scope));
        return TypedResults.Ok(result);
    }

    [EndpointSummary("Download a report result")]
    [EndpointDescription("Returns the PDF for a succeeded execution in the caller's scope.")]
    public static async Task<IResult> DownloadReport(ISender sender, Guid id)
    {
        var pdf = await sender.Send(new DownloadReportResultQuery(id));
        return Results.File(pdf.Content, "application/pdf");
    }
}
