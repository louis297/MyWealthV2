using MyWealthV2.Application.CorporateActions;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.MarketData;
using MyWealthV2.Domain.Enums;
using static MyWealthV2.Application.FunctionalTests.CorporateActions.CorporateActionFeedHttp;
using static MyWealthV2.Application.FunctionalTests.CorporateActions.CorporateActionHttp;

namespace MyWealthV2.Application.FunctionalTests.CorporateActions;

/// <summary>Dividends R8–R10: dividends on the corporate action feed.</summary>
[Parallelizable]
public class DividendFeedTests : TestBase
{
    private static CorporateActionFeedItem Dividend(
        string actionId = "w-CA3",
        int version = 1,
        PartyActionStatus status = PartyActionStatus.Announced,
        decimal? amount = 1.00m,
        DividendTaxSource? taxSource = DividendTaxSource.Foreign,
        decimal? credit = 0m,
        decimal? rate = 15m,
        decimal? price = 25m,
        int? ratioNew = null,
        int? ratioHeld = null) =>
        new($"{actionId}-V{version}", actionId, version, status, "ACME.SIM", "USD", CorporateActionKind.Dividend,
            ratioNew, ratioHeld, new DateOnly(2026, 11, 26), null, new DateOnly(2026, 11, 30),
            amount, taxSource, credit, rate, price);

    [Test]
    public async Task ADividendItem_IsStoredWithItsTerms_AndCreatesAFeedDividendWithItsPlanPrice()
    {
        var tenant = await TestApp.CreateTenantAsync("North", "north-dv");
        await MarketDataHttp.AddInstrumentAsync(tenant.Id, "ACME.SIM", "USD");

        await DeliverAsync(Dividend());

        var announcement = (await AnnouncementsAsync()).Single();
        (announcement.Kind, announcement.RatioNew, announcement.RatioHeld)
            .ShouldBe(((CorporateActionKind?)CorporateActionKind.Dividend, (int?)null, (int?)null));
        (announcement.AmountPerShare, announcement.TaxSource, announcement.ImputationCreditPerShare,
                announcement.ForeignTaxRatePercent, announcement.ReinvestmentPrice)
            .ShouldBe(((decimal?)1.00m, (DividendTaxSource?)DividendTaxSource.Foreign, (decimal?)0m, (decimal?)15m, (decimal?)25m));
        announcement.Status.ShouldBe(AnnouncementStatus.Processed);

        var action = (await ActionsOfAsync(tenant.Id)).Single();
        (action.Kind, action.Origin, action.Status, action.PartyActionId)
            .ShouldBe((CorporateActionKind.Dividend, CorporateActionOrigin.Feed, CorporateActionStatus.Announced, "w-CA3"));
        action.Terms!.ReinvestmentPrice.ShouldBe(25m);
        action.PaymentDate.ShouldBe(new DateOnly(2026, 11, 30));
    }

    [Test]
    public async Task ADividendItem_WithoutAnAmountOrATaxSource_OrWithARatio_RefusesThePage()
    {
        foreach (var item in new[] { Dividend(amount: null), Dividend(taxSource: null), Dividend(ratioNew: 2, ratioHeld: 1) })
        {
            Feed.Reset();
            Feed.Add(Connection, item);

            (await PullAsync()).Failure.ShouldBe(MarketDataFailure.InvalidItem);
            (await AnnouncementsAsync()).ShouldBeEmpty();
        }
    }

    [Test]
    public async Task ASplitItem_WithoutARatio_StillRefusesThePage()
    {
        Feed.Add(Connection, Item(ratioNew: null));

        (await PullAsync()).Failure.ShouldBe(MarketDataFailure.InvalidItem);
    }

    [Test]
    public async Task TermsThatBreakTheRule_AreSkippedAsInvalid()
    {
        var tenant = await TestApp.CreateTenantAsync("North", "north-dv");
        await MarketDataHttp.AddInstrumentAsync(tenant.Id, "ACME.SIM", "USD");

        await DeliverAsync(Dividend(credit: 0.10m), Dividend("w-CA4", amount: 0m), Dividend("w-CA5", rate: 100m));

        (await AnnouncementsAsync()).ShouldAllBe(row => row.SkipReason == AnnouncementSkipReason.Invalid);
        (await ActionsOfAsync(tenant.Id)).ShouldBeEmpty();
    }

    [Test]
    public async Task ALaterAnnouncedVersion_ReplacesTheTermsAndDates()
    {
        var tenant = await TestApp.CreateTenantAsync("North", "north-dv");
        await MarketDataHttp.AddInstrumentAsync(tenant.Id, "ACME.SIM", "USD");
        await DeliverAsync(Dividend());

        await DeliverAsync(Dividend(version: 2, amount: 1.10m, price: 26m));

        var action = (await ActionsOfAsync(tenant.Id)).Single();
        (action.AmountPerShare, action.ReinvestmentPrice, action.FeedVersion).ShouldBe(((decimal?)1.10m, (decimal?)26m, (int?)2));
    }

    [Test]
    public async Task TheEffectiveVersion_MakesTheDividendGoEx_AndALaterVersionIsAlreadyEffective()
    {
        var tenant = await TestApp.CreateTenantAsync("North", "north-dv");
        await MarketDataHttp.AddInstrumentAsync(tenant.Id, "ACME.SIM", "USD");
        await DeliverAsync(Dividend(), Dividend(version: 2, status: PartyActionStatus.Effective));

        await DeliverAsync(Dividend(version: 3, status: PartyActionStatus.Cancelled));

        (await ActionsOfAsync(tenant.Id)).Single().Status.ShouldNotBe(CorporateActionStatus.Cancelled);
        (await AnnouncementsAsync()).Last().SkipReason.ShouldBe(AnnouncementSkipReason.AlreadyEffective);
    }

    [Test]
    public async Task TheAnnouncementItem_ShowsTheTerms()
    {
        Feed.Add(Connection, Dividend());
        (await PullAsync()).Failure.ShouldBeNull();
        var system = await AccountHttp.SignInSystemAdmin();

        var item = (await GetAsync(system.AccessToken, "/corporate-action-announcements")).GetProperty("items")[0];

        item.GetProperty("kind").GetString().ShouldBe("Dividend");
        item.GetProperty("ratioNew").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
        item.GetProperty("amountPerShare").GetDecimal().ShouldBe(1.00m);
        item.GetProperty("taxSource").GetString().ShouldBe("Foreign");
        item.GetProperty("imputationCreditPerShare").GetDecimal().ShouldBe(0m);
        item.GetProperty("foreignTaxRatePercent").GetDecimal().ShouldBe(15m);
        item.GetProperty("reinvestmentPrice").GetDecimal().ShouldBe(25m);
    }
}
