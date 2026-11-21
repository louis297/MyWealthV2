using MyWealthV2.Application.BankReconciliations;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MyWealthV2.Web.Endpoints;

/// <summary>Bank reconciliation (docs/features/bank-reconciliation.md §4.3–§4.5, §8). TenantAdmin only.</summary>
public class BankReconciliations : IEndpointGroup
{
    public static string RoutePrefix => "/bank-reconciliations";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapPost(RunBankReconciliation).RequireAuthorization(Policies.BankReconciliationsManage);
        groupBuilder.MapGet(GetBankReconciliation, "{id}").RequireAuthorization(Policies.BankReconciliationsManage);
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
}
