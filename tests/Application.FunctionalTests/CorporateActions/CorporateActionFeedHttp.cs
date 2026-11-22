using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.CorporateActions;
using MyWealthV2.Application.FunctionalTests.MarketData;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.CorporateActions;

/// <summary>Helpers for the corporate action feed tests (corporate-actions §10): the fake feed of the
/// <c>simulator</c> connection, its items and the stored announcements.</summary>
internal static class CorporateActionFeedHttp
{
    public const string Connection = MarketDataHttp.Connection;

    public static InMemoryCorporateActionFeed Feed =>
        FunctionalTestSetup.WebApiServices.GetRequiredService<InMemoryCorporateActionFeed>();

    public static CorporateActionFeedItem Item(
        string actionId = "w-CA1",
        int version = 1,
        PartyActionStatus? status = PartyActionStatus.Announced,
        string? symbol = "ACME.SIM",
        string? currency = "USD",
        CorporateActionKind? kind = CorporateActionKind.Split,
        int? ratioNew = 2,
        int? ratioHeld = 1,
        DateOnly? exDate = null,
        string? id = null) =>
        new(id ?? $"{actionId}-V{version}", actionId, version, status, symbol, currency, kind, ratioNew, ratioHeld,
            exDate ?? new DateOnly(2026, 11, 24), null, null);

    public static async Task<CorporateActionPullOutcome> PullAsync()
    {
        await using var scope = FunctionalTestSetup.WebApiServices.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PullCorporateActions>().RunAsync(Connection);
    }

    public static async Task<List<CorporateActionAnnouncement>> AnnouncementsAsync()
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.CorporateActionAnnouncements.AsNoTracking().OrderBy(row => row.Id).ToListAsync();
    }

    public static async Task<MarketDataFeed?> FeedStateAsync()
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.MarketDataFeeds.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Connection == Connection && row.Kind == MarketDataFeedKind.CorporateActions);
    }
}
