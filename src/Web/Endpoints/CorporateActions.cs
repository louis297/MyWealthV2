using Microsoft.AspNetCore.Http.HttpResults;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.CorporateActions;

namespace MyWealthV2.Web.Endpoints;

/// <summary>
/// Corporate actions of the caller's Tenant (docs/features/corporate-actions.md §8, api-design §7.11). No caller
/// names a tenant. <c>{id}</c> takes a GUID only.
/// </summary>
public class CorporateActions : IEndpointGroup
{
    public static string RoutePrefix => "/corporate-actions";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(GetCorporateActions).RequireAuthorization(Policies.CorporateActionsRead);
        groupBuilder.MapGet(GetCorporateAction, "{id:guid}").RequireAuthorization(Policies.CorporateActionsRead);
        groupBuilder.MapGet(GetCorporateActionEntitlements, "{id:guid}/entitlements").RequireAuthorization(Policies.CorporateActionsRead);
        groupBuilder.MapPost(CreateCorporateAction).RequireAuthorization(Policies.CorporateActionsManage);
        groupBuilder.MapPut(UpdateCorporateAction, "{id:guid}").RequireAuthorization(Policies.CorporateActionsManage);
        groupBuilder.MapPost(CancelCorporateAction, "{id:guid}/cancel").RequireAuthorization(Policies.CorporateActionsManage);
        groupBuilder.MapPost(ApplyCorporateAction, "{id:guid}/apply").RequireAuthorization(Policies.CorporateActionsManage);
    }

    [EndpointSummary("List corporate actions")]
    [EndpointDescription("Returns the tenant's corporate actions, latest ex-date first. Filters: status (comma-separated), instrumentId, origin.")]
    public static async Task<Ok<PagedList<CorporateActionDto>>> GetCorporateActions(
        ISender sender,
        int page = 1,
        int pageSize = 20,
        string? status = null,
        Guid? instrumentId = null,
        string? origin = null,
        string? tenantId = null,
        string? kind = null) =>
        TypedResults.Ok(await sender.Send(new GetCorporateActionsQuery(page, pageSize, status, instrumentId, origin, tenantId, kind)));

    [EndpointSummary("Get a corporate action")]
    [EndpointDescription("Returns one corporate action by PublicId, with the counts of its entitlements.")]
    public static async Task<Ok<CorporateActionDto>> GetCorporateAction(ISender sender, Guid id) =>
        TypedResults.Ok(await sender.Send(new GetCorporateActionByIdQuery(id)));

    [EndpointSummary("List the entitlements of a corporate action")]
    [EndpointDescription("Returns the entitlements the caller can see, in account order. Filter: status (comma-separated).")]
    public static async Task<Ok<PagedList<CorporateActionEntitlementDto>>> GetCorporateActionEntitlements(
        ISender sender,
        Guid id,
        int page = 1,
        int pageSize = 20,
        string? status = null) =>
        TypedResults.Ok(await sender.Send(new GetCorporateActionEntitlementsQuery(id, page, pageSize, status)));

    [EndpointSummary("Enter a corporate action")]
    [EndpointDescription("Enters a split, consolidation or bonus issue of an instrument no market data connection serves. It is Announced.")]
    public static async Task<Created<CreatedCorporateActionDto>> CreateCorporateAction(ISender sender, CreateCorporateActionCommand command)
    {
        var id = await sender.Send(command);
        return TypedResults.Created($"/corporate-actions/{id}", new CreatedCorporateActionDto(id));
    }

    [EndpointSummary("Change a corporate action")]
    [EndpointDescription("Replaces the kind, ratio, dates and memo of a hand-entered corporate action that is still Announced.")]
    public static async Task<NoContent> UpdateCorporateAction(ISender sender, Guid id, UpdateCorporateActionCommand command)
    {
        command.Id = id;
        await sender.Send(command);
        return TypedResults.NoContent();
    }

    [EndpointSummary("Cancel a corporate action")]
    [EndpointDescription("Cancels a hand-entered corporate action that is still Announced, with a note.")]
    public static async Task<NoContent> CancelCorporateAction(ISender sender, Guid id, CancelCorporateActionCommand command)
    {
        command.Id = id;
        await sender.Send(command);
        return TypedResults.NoContent();
    }

    [EndpointSummary("Apply a corporate action now")]
    [EndpointDescription("Makes a hand-entered corporate action effective on or after its ex-date, for an instrument whose price does not arrive.")]
    public static async Task<NoContent> ApplyCorporateAction(ISender sender, Guid id, ApplyCorporateActionCommand command)
    {
        command.Id = id;
        await sender.Send(command);
        return TypedResults.NoContent();
    }
}
