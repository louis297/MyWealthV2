using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.MarketData;
using MyWealthV2.Application.FunctionalTests.NominatedBankAccounts;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;
using static MyWealthV2.Application.FunctionalTests.CorporateActions.CorporateActionFeedHttp;
using static MyWealthV2.Application.FunctionalTests.CorporateActions.CorporateActionHttp;

namespace MyWealthV2.Application.FunctionalTests.CorporateActions;

/// <summary>Corporate-actions R11–R13: processing stored announcements into corporate actions in every Tenant.</summary>
[Parallelizable]
public class ProcessCorporateActionAnnouncementsTests : TestBase
{
    [Test]
    public async Task AnAnnouncedItem_CreatesOneAnnouncedActionInEachTenantListingTheSymbolInThatCurrency()
    {
        var north = await TestApp.CreateTenantAsync("North", "north-ca");
        var south = await TestApp.CreateTenantAsync("South", "south-ca");
        var east = await TestApp.CreateTenantAsync("East", "east-ca");
        var northAcme = await MarketDataHttp.AddInstrumentAsync(north.Id, "ACME.SIM", "USD");
        var southAcme = await MarketDataHttp.AddInstrumentAsync(south.Id, "acme.sim", "USD", active: false);
        await MarketDataHttp.AddInstrumentAsync(east.Id, "ACME.SIM", "NZD");

        await DeliverAsync(Item(exDate: new DateOnly(2026, 11, 24)));

        var northAction = (await ActionsOfAsync(north.Id)).Single();
        (northAction.InstrumentId, northAction.Origin, northAction.Status, northAction.Kind, northAction.RatioNew, northAction.RatioHeld)
            .ShouldBe((northAcme.Id, CorporateActionOrigin.Feed, CorporateActionStatus.Announced, CorporateActionKind.Split, 2, 1));
        (northAction.Connection, northAction.PartyActionId, northAction.FeedVersion, northAction.ExDate)
            .ShouldBe((Connection, "w-CA1", (int?)1, new DateOnly(2026, 11, 24)));
        (await ActionsOfAsync(south.Id)).Single().InstrumentId.ShouldBe(southAcme.Id);
        (await ActionsOfAsync(east.Id)).ShouldBeEmpty();
        var announcement = (await AnnouncementsAsync()).Single();
        announcement.Status.ShouldBe(AnnouncementStatus.Processed);
        announcement.ProcessedAt.ShouldNotBeNull();
    }

    [Test]
    public async Task ALaterVersion_ReplacesTheRatioAndDates_AndAnOlderOneIsSuperseded()
    {
        var tenant = await TestApp.CreateTenantAsync("North", "north-ca");
        await MarketDataHttp.AddInstrumentAsync(tenant.Id, "ACME.SIM", "USD");
        await DeliverAsync(Item(version: 1), Item(version: 3, ratioNew: 3, exDate: new DateOnly(2026, 11, 30)));

        await DeliverAsync(Item(version: 2, ratioNew: 5));

        var action = (await ActionsOfAsync(tenant.Id)).Single();
        (action.RatioNew, action.ExDate, action.FeedVersion).ShouldBe((3, new DateOnly(2026, 11, 30), (int?)3));
        var announcements = await AnnouncementsAsync();
        announcements.Select(row => row.Status).ShouldBe([AnnouncementStatus.Processed, AnnouncementStatus.Processed, AnnouncementStatus.Skipped]);
        announcements[2].SkipReason.ShouldBe(AnnouncementSkipReason.Superseded);
    }

    [Test]
    public async Task ACancelledVersion_CancelsTheAction_AndOneForNoActionChangesNothing()
    {
        var tenant = await TestApp.CreateTenantAsync("North", "north-ca");
        await MarketDataHttp.AddInstrumentAsync(tenant.Id, "ACME.SIM", "USD");
        await DeliverAsync(Item(version: 1), Item(version: 2, status: PartyActionStatus.Cancelled));
        await DeliverAsync(Item("w-CA2", version: 1, status: PartyActionStatus.Cancelled));

        var action = (await ActionsOfAsync(tenant.Id)).Single();
        action.Status.ShouldBe(CorporateActionStatus.Cancelled);
        (action.CancelledByUserId, action.CancelNote, action.FeedVersion).ShouldBe(((int?)null, (string?)null, (int?)2));
        action.CancelledAt.ShouldNotBeNull();
        var announcements = await AnnouncementsAsync();
        announcements[2].Status.ShouldBe(AnnouncementStatus.Skipped);
    }

    [Test]
    public async Task AnEffectiveVersion_MakesTheActionEffective_WithAnEntitlementPerCustodialHolder()
    {
        var world = await NominatedWorld.CreateAsync();
        var holders = await HoldersAsync(world, "ACME.SIM");
        await DeliverAsync(Item(version: 1));

        await DeliverAsync(Item(version: 2, status: PartyActionStatus.Effective, ratioNew: 4));

        var action = (await ActionsOfAsync(world.Tenant.Id)).Single();
        (action.Status, action.RatioNew, action.FeedVersion, action.EffectiveByUserId)
            .ShouldBe((CorporateActionStatus.Effective, 4, (int?)2, (int?)null));
        action.EffectiveAt.ShouldNotBeNull();
        (await EntitlementsAsync(action.PublicId)).Select(row => row.AccountId)
            .ShouldBe([await AccountIdAsync(holders.Ned), await AccountIdAsync(holders.Olga)]);
    }

    [Test]
    public async Task AnEffectiveVersionWithNoActionYet_CreatesItAndMakesItEffective()
    {
        var world = await NominatedWorld.CreateAsync();
        var holders = await HoldersAsync(world, "ACME.SIM");

        await DeliverAsync(Item(version: 3, status: PartyActionStatus.Effective));

        var action = (await ActionsOfAsync(world.Tenant.Id)).Single();
        (action.Status, action.FeedVersion).ShouldBe((CorporateActionStatus.Effective, (int?)3));
        (await EntitlementsAsync(action.PublicId)).Count.ShouldBe(2);
        await CorporateActionRuns.RunApplyAsync(Connection);
        (await ItemAsync(world.TenantAdmin.AccessToken, action.PublicId)).GetProperty("status").GetString().ShouldBe("Applied");
        (await EntitlementsAsync(action.PublicId)).Single(row => row.AccountId == AccountIdAsync(holders.Ned).Result)
            .QuantityAfter.ShouldBe(200m);
    }

    [Test]
    public async Task TheSkipReasons_ApplyInOrder()
    {
        var tenant = await TestApp.CreateTenantAsync("North", "north-ca");
        await MarketDataHttp.AddInstrumentAsync(tenant.Id, "ACME.SIM", "USD");
        await MarketDataHttp.AddInstrumentAsync(tenant.Id, "AAPL", "USD");

        await DeliverAsync(
            Item("w-CA1", kind: null),
            Item("w-CA2", ratioNew: 1, ratioHeld: 2),
            Item("w-CA3", symbol: "AAPL"),
            Item("w-CA4", symbol: "NOBODY.SIM"));

        (await AnnouncementsAsync()).Select(row => row.SkipReason).ShouldBe([
            AnnouncementSkipReason.UnsupportedKind,
            AnnouncementSkipReason.Invalid,
            AnnouncementSkipReason.Invalid,
            AnnouncementSkipReason.NoInstrument
        ]);
        (await ActionsOfAsync(tenant.Id)).ShouldBeEmpty();
    }

    [Test]
    public async Task ACancellationAfterTheActionWasApplied_IsSkippedAsAlreadyEffective_AndChangesNothing()
    {
        var tenant = await TestApp.CreateTenantAsync("North", "north-ca");
        await MarketDataHttp.AddInstrumentAsync(tenant.Id, "ACME.SIM", "USD");
        await DeliverAsync(Item(version: 1, status: PartyActionStatus.Effective));
        await CorporateActionRuns.RunApplyAsync();
        (await ActionsOfAsync(tenant.Id)).Single().Status.ShouldBe(CorporateActionStatus.Applied);

        await DeliverAsync(Item(version: 2, status: PartyActionStatus.Cancelled));

        (await ActionsOfAsync(tenant.Id)).Single().Status.ShouldBe(CorporateActionStatus.Applied);
        var last = (await AnnouncementsAsync()).Last();
        (last.Status, last.SkipReason).ShouldBe((AnnouncementStatus.Skipped, (AnnouncementSkipReason?)AnnouncementSkipReason.AlreadyEffective));
    }

    [Test]
    public async Task AnAnnouncementOfAnotherKind_ChangesNoAction()
    {
        var tenant = await TestApp.CreateTenantAsync("North", "north-ca");
        await MarketDataHttp.AddInstrumentAsync(tenant.Id, "ACME.SIM", "USD");
        await DeliverAsync(Item(version: 1));

        await DeliverAsync(Item(version: 2, kind: CorporateActionKind.Bonus, ratioNew: 1, ratioHeld: 5));

        var action = (await ActionsOfAsync(tenant.Id)).Single();
        (action.Kind, action.RatioNew, action.FeedVersion).ShouldBe((CorporateActionKind.Split, 2, (int?)1));
        (await AnnouncementsAsync()).Last().Status.ShouldBe(AnnouncementStatus.Skipped);
    }

    [Test]
    public async Task ADisabledTenantsInstrument_GetsTheAction()
    {
        var tenant = await TestApp.CreateTenantAsync("North", "north-ca");
        await MarketDataHttp.AddInstrumentAsync(tenant.Id, "ACME.SIM", "USD");
        await ExecuteSqlAsync($"UPDATE [Tenants] SET [IsActive] = 0 WHERE [Id] = {tenant.Id}");

        await DeliverAsync(Item());

        (await ActionsOfAsync(tenant.Id)).Count.ShouldBe(1);
    }

    private static async Task ExecuteSqlAsync(string sql)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlRawAsync(sql);
    }
}
