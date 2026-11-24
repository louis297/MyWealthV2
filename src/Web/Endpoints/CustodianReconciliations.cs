using Microsoft.AspNetCore.Http.HttpResults;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.CustodianReconciliations;

namespace MyWealthV2.Web.Endpoints;

/// <summary>Custodian reconciliations of the Tenant's safekeeping accounts (docs/features/in-kind-transfers.md R50–R59).</summary>
public class CustodianReconciliations : IEndpointGroup
{
    public static string RoutePrefix => "/custodian-reconciliations";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(GetOverview, "overview").RequireAuthorization(Policies.CustodianReconciliationsManage);
        groupBuilder.MapGet(GetCustodianReconciliations).RequireAuthorization(Policies.CustodianReconciliationsManage);
        groupBuilder.MapGet(GetCustodianReconciliation, "{id}").RequireAuthorization(Policies.CustodianReconciliationsManage);
        groupBuilder.MapPost(RunCustodianReconciliation).RequireAuthorization(Policies.CustodianReconciliationsManage);
    }

    [EndpointSummary("Custodian reconciliation overview")]
    [EndpointDescription("One entry per safekeeping account that is active or has a run, with its latest run (R59).")]
    public static async Task<Ok<PagedList<CustodianReconciliationOverviewDto>>> GetOverview(ISender sender, Guid? tenantId = null) =>
        TypedResults.Ok(await sender.Send(new GetCustodianReconciliationOverviewQuery(tenantId)));

    [EndpointSummary("List custodian reconciliations")]
    [EndpointDescription("The Tenant's runs, newest first (R59).")]
    public static async Task<Ok<PagedList<CustodianReconciliationListItemDto>>> GetCustodianReconciliations(
        ISender sender,
        int page = 1,
        int pageSize = 20,
        Guid? safekeepingAccountId = null,
        string? status = null,
        DateOnly? from = null,
        DateOnly? to = null,
        Guid? tenantId = null) =>
        TypedResults.Ok(await sender.Send(new GetCustodianReconciliationsQuery(page, pageSize, safekeepingAccountId, status, from, to, tenantId)));

    [EndpointSummary("Get a custodian reconciliation")]
    [EndpointDescription("One run with every line (R59).")]
    public static async Task<Ok<CustodianReconciliationDto>> GetCustodianReconciliation(ISender sender, Guid id) =>
        TypedResults.Ok(await sender.Send(new GetCustodianReconciliationByIdQuery(id)));

    [EndpointSummary("Run a custodian reconciliation now")]
    [EndpointDescription("Runs a custodian reconciliation of one active safekeeping account at once (R58).")]
    public static async Task<Created<CreatedCustodianReconciliationDto>> RunCustodianReconciliation(
        ISender sender, RunCustodianReconciliationCommand command)
    {
        var id = await sender.Send(command);
        return TypedResults.Created($"{RoutePrefix}/{id}", new CreatedCustodianReconciliationDto(id));
    }
}

public sealed record CreatedCustodianReconciliationDto(Guid Id);
