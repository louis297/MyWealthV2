using Microsoft.AspNetCore.Http.HttpResults;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.CorporateActions;

namespace MyWealthV2.Web.Endpoints;

/// <summary>The stored corporate action announcements, platform data (docs/features/corporate-actions.md R14).</summary>
public class CorporateActionAnnouncements : IEndpointGroup
{
    public static string RoutePrefix => "/corporate-action-announcements";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(GetCorporateActionAnnouncements).RequireAuthorization(Policies.TenantsManage);
    }

    [EndpointSummary("List corporate action announcements")]
    [EndpointDescription("Returns the announcements read from the market data connections, newest first, with how each was processed. Filters: connection, status (comma-separated). SystemAdmin only.")]
    public static async Task<Ok<PagedList<CorporateActionAnnouncementDto>>> GetCorporateActionAnnouncements(
        ISender sender,
        int page = 1,
        int pageSize = 20,
        string? connection = null,
        string? status = null) =>
        TypedResults.Ok(await sender.Send(new GetCorporateActionAnnouncementsQuery(page, pageSize, connection, status)));
}
