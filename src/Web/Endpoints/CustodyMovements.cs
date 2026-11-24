using Microsoft.AspNetCore.Http.HttpResults;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.CustodyMovements;

namespace MyWealthV2.Web.Endpoints;

/// <summary>The custodian's movements of the Tenant's safekeeping accounts (docs/features/in-kind-transfers.md R18, R19).</summary>
public class CustodyMovements : IEndpointGroup
{
    public static string RoutePrefix => "/custody-movements";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(GetCustodyMovements).RequireAuthorization(Policies.CustodyMovementsManage);
        groupBuilder.MapGet(GetCustodyMovement, "{id}").RequireAuthorization(Policies.CustodyMovementsManage);
        groupBuilder.MapPost(IgnoreCustodyMovement, "{id}/ignore").RequireAuthorization(Policies.CustodyMovementsManage);
        groupBuilder.MapPost(ReopenCustodyMovement, "{id}/reopen").RequireAuthorization(Policies.CustodyMovementsManage);
    }

    [EndpointSummary("List custody movements")]
    [EndpointDescription("Returns the Tenant's custody movements, newest received first (R19). TenantAdmin only.")]
    public static async Task<Ok<PagedList<CustodyMovementDto>>> GetCustodyMovements(
        ISender sender,
        int page = 1,
        int pageSize = 20,
        string? status = null,
        string? direction = null,
        string? kind = null,
        Guid? instrumentId = null,
        Guid? safekeepingAccountId = null,
        Guid? tenantId = null) =>
        TypedResults.Ok(await sender.Send(new GetCustodyMovementsQuery(page, pageSize, status, direction, kind, instrumentId,
            safekeepingAccountId, tenantId)));

    [EndpointSummary("Get a custody movement")]
    [EndpointDescription("Returns one custody movement of the Tenant (R19).")]
    public static async Task<Ok<CustodyMovementDto>> GetCustodyMovement(ISender sender, Guid id) =>
        TypedResults.Ok(await sender.Send(new GetCustodyMovementByIdQuery(id)));

    [EndpointSummary("Ignore a custody movement")]
    [EndpointDescription("Records that a TenantAdmin has dealt with a movement in suspense, with a note (R18).")]
    public static async Task<NoContent> IgnoreCustodyMovement(ISender sender, Guid id, IgnoreCustodyMovementCommand command)
    {
        command.Id = id;
        await sender.Send(command);
        return TypedResults.NoContent();
    }

    [EndpointSummary("Reopen a custody movement")]
    [EndpointDescription("Puts an ignored movement back in suspense (R18).")]
    public static async Task<NoContent> ReopenCustodyMovement(ISender sender, Guid id, ReopenCustodyMovementCommand command)
    {
        command.Id = id;
        await sender.Send(command);
        return TypedResults.NoContent();
    }
}
