using MyWealthV2.Application.BankReconciliations;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Web.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MyWealthV2.Web.Endpoints;

/// <summary>Bank reconciliation (docs/features/bank-reconciliation.md §4.3–§4.5, §8). TenantAdmin only.</summary>
public class BankReconciliations : IEndpointGroup
{
    public static string RoutePrefix => "/bank-reconciliations";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        // R1: a query or body field the route does not define is a 400.
        groupBuilder.MapGet(GetBankReconciliationOverview, "overview").RequireAuthorization(Policies.BankReconciliationsManage)
            .AddEndpointFilter(new KnownQueryKeysFilter("tenantId"));
        groupBuilder.MapGet(GetBankReconciliations).RequireAuthorization(Policies.BankReconciliationsManage)
            .AddEndpointFilter(new KnownQueryKeysFilter(
                "page", "pageSize", "clientMoneyAccountId", "status", "from", "to", "tenantId"));
        groupBuilder.MapPost(RunBankReconciliation).RequireAuthorization(Policies.BankReconciliationsManage)
            .AddEndpointFilter(new KnownQueryKeysFilter());
        groupBuilder.MapGet(GetBankReconciliation, "{id}").RequireAuthorization(Policies.BankReconciliationsManage)
            .AddEndpointFilter(new KnownQueryKeysFilter());
    }

    [EndpointSummary("Run a bank reconciliation now")]
    [EndpointDescription("Reconciles one client money account of the current tenant at once and stores the run (R17).")]
    public static async Task<Created<CreatedIdDto>> RunBankReconciliation(ISender sender, RunBankReconciliationCommand command)
    {
        var id = await sender.Send(command);
        return TypedResults.Created($"{RoutePrefix}/{id}", new CreatedIdDto(id));
    }

    [EndpointSummary("Get a bank reconciliation")]
    [EndpointDescription("Returns one run of the current tenant with its items (R21). TenantAdmin only.")]
    public static async Task<Ok<BankReconciliationDto>> GetBankReconciliation(ISender sender, Guid id) =>
        TypedResults.Ok(await sender.Send(new GetBankReconciliationByIdQuery(id)));

    [EndpointSummary("Overview of bank reconciliations")]
    [EndpointDescription("Returns each client money account of the current tenant with its latest run (R19). TenantAdmin only.")]
    public static async Task<Ok<PagedList<BankReconciliationOverviewDto>>> GetBankReconciliationOverview(
        ISender sender,
        Guid? tenantId = null) =>
        TypedResults.Ok(await sender.Send(new GetBankReconciliationOverviewQuery(tenantId)));

    [EndpointSummary("List bank reconciliations")]
    [EndpointDescription("Returns the current tenant's runs, newest first, with optional filters (R20). TenantAdmin only.")]
    public static async Task<Ok<PagedList<BankReconciliationListItemDto>>> GetBankReconciliations(
        ISender sender,
        int page = 1,
        int pageSize = 20,
        Guid? clientMoneyAccountId = null,
        string? status = null,
        DateOnly? from = null,
        DateOnly? to = null,
        Guid? tenantId = null) =>
        TypedResults.Ok(await sender.Send(
            new GetBankReconciliationsQuery(page, pageSize, clientMoneyAccountId, status, from, to, tenantId)));
}
