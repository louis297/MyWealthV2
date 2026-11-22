using MyWealthV2.Application.MarketData;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.MarketData;

/// <summary>Simulated instruments R8–R11, R13: a run reads every price of a connection and stores the served ones
/// under the platform's UTC date.</summary>
[Parallelizable]
public class SyncMarketPricesTests : TestBase
{
    [Test]
    public async Task ARun_StoresOneRowPerActiveServedInstrument_InEveryTenant_DatedThePlatformsToday()
    {
        var north = await TestApp.CreateTenantAsync("North Advisory", "north-advisory");
        var south = await TestApp.CreateTenantAsync("South Co", "south-co");
        var northAcme = await MarketDataHttp.AddInstrumentAsync(north.Id, "ACME.SIM", "USD");
        var southAcme = await MarketDataHttp.AddInstrumentAsync(south.Id, "ACME.SIM", "USD");
        MarketDataHttp.Prices.Answer(MarketDataHttp.Item("acme.sim", "USD", 101.37m, date: new DateOnly(2031, 5, 6)));
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        var outcome = await MarketDataHttp.RunAsync();

        outcome.Failed.ShouldBeFalse();
        outcome.Stored.ShouldBe(2);
        foreach (var instrument in new[] { northAcme, southAcme })
        {
            var row = (await MarketDataHttp.PricesOfAsync(instrument.Id)).ShouldHaveSingleItem();
            row.AsOfDate.ShouldBe(MarketDataHttp.Today);
            row.Price.ShouldBe(101.37m);
            row.Source.ShouldBe("Simulator");
            row.FetchedAt.ShouldBeGreaterThan(before);
        }

        var feed = (await MarketDataHttp.FeedAsync()).ShouldNotBeNull();
        feed.LastSucceededAt.ShouldNotBeNull().ShouldBeGreaterThan(before);
        feed.LastFailedAt.ShouldBeNull();
        feed.LastFailure.ShouldBeNull();
        MarketDataHttp.Prices.Requests.ShouldBe([MarketDataHttp.Connection]);
    }

    [Test]
    public async Task ASecondRunTheSameDay_ReplacesTodaysRow_AndTheFirstRunOfTheNextDay_AddsARow()
    {
        var tenant = await TestApp.CreateTenantAsync("North Advisory", "north-advisory");
        var acme = await MarketDataHttp.AddInstrumentAsync(tenant.Id, "ACME.SIM", "USD");
        var morning = new DateTimeOffset(2026, 11, 22, 1, 0, 0, TimeSpan.Zero);

        MarketDataHttp.Prices.Answer(MarketDataHttp.Item("ACME.SIM", "USD", 100m));
        await MarketDataHttp.RunAsync(morning);
        MarketDataHttp.Prices.Answer(MarketDataHttp.Item("ACME.SIM", "USD", 102.5m));
        await MarketDataHttp.RunAsync(morning.AddHours(5));
        MarketDataHttp.Prices.Answer(MarketDataHttp.Item("ACME.SIM", "USD", 99.0001m));
        await MarketDataHttp.RunAsync(morning.AddDays(1));

        var rows = await MarketDataHttp.PricesOfAsync(acme.Id);
        rows.Select(row => (row.AsOfDate, row.Price)).ShouldBe(
        [
            (new DateOnly(2026, 11, 22), 102.5m),
            (new DateOnly(2026, 11, 23), 99.0001m)
        ]);
        rows[0].FetchedAt.ShouldBe(morning.AddHours(5));
    }

    [Test]
    public async Task AnInactiveInstrument_AnotherQuoteCurrency_AndAnUnservedSymbol_GetNoRow()
    {
        var tenant = await TestApp.CreateTenantAsync("North Advisory", "north-advisory");
        var inactive = await MarketDataHttp.AddInstrumentAsync(tenant.Id, "ACME.SIM", "USD", active: false);
        var euro = await MarketDataHttp.AddInstrumentAsync(tenant.Id, "KIWI.SIM", "EUR");
        var real = await MarketDataHttp.AddInstrumentAsync(tenant.Id, "AAPL.US", "USD");
        MarketDataHttp.Prices.Answer(
            MarketDataHttp.Item("ACME.SIM", "USD", 100m),
            MarketDataHttp.Item("KIWI.SIM", "NZD", 5m),
            MarketDataHttp.Item("AAPL.US", "USD", 230m));

        var outcome = await MarketDataHttp.RunAsync();

        outcome.Failed.ShouldBeFalse();
        outcome.Stored.ShouldBe(0);
        (await MarketDataHttp.PricesOfAsync(inactive.Id)).ShouldBeEmpty();
        (await MarketDataHttp.PricesOfAsync(euro.Id)).ShouldBeEmpty();
        (await MarketDataHttp.PricesOfAsync(real.Id)).ShouldBeEmpty();
        (await MarketDataHttp.FeedAsync()).ShouldNotBeNull().LastSucceededAt.ShouldNotBeNull();
    }

    [Test]
    public async Task BadItems_AreSkipped_AndTheOtherItemsStored()
    {
        var tenant = await TestApp.CreateTenantAsync("North Advisory", "north-advisory");
        var acme = await MarketDataHttp.AddInstrumentAsync(tenant.Id, "ACME.SIM", "USD");
        var kiwi = await MarketDataHttp.AddInstrumentAsync(tenant.Id, "KIWI.SIM", "NZD");
        var zero = await MarketDataHttp.AddInstrumentAsync(tenant.Id, "ZERO.SIM", "USD");
        var fine = await MarketDataHttp.AddInstrumentAsync(tenant.Id, "FINE.SIM", "USD");
        var none = await MarketDataHttp.AddInstrumentAsync(tenant.Id, "NONE.SIM", "USD");
        MarketDataHttp.Prices.Answer(
            new MarketPriceItem("", "USD", 1m, null),
            new MarketPriceItem(new string('A', 29) + ".SIM", "USD", 1m, null),
            new MarketPriceItem("ACME.SIM", "US", 100m, null),
            new MarketPriceItem("KIWI.SIM", "NZD", null, null),
            MarketDataHttp.Item("ZERO.SIM", "USD", 0m),
            MarketDataHttp.Item("FINE.SIM", "USD", 1.123456789m),
            MarketDataHttp.Item("NONE.SIM", "USD", -2m),
            MarketDataHttp.Item("KIWI.SIM", "NZD", 5.12345678m));

        var outcome = await MarketDataHttp.RunAsync();

        outcome.Failed.ShouldBeFalse();
        outcome.Stored.ShouldBe(1);
        (await MarketDataHttp.PricesOfAsync(kiwi.Id)).ShouldHaveSingleItem().Price.ShouldBe(5.12345678m);
        (await MarketDataHttp.PricesOfAsync(acme.Id)).ShouldBeEmpty();
        (await MarketDataHttp.PricesOfAsync(zero.Id)).ShouldBeEmpty();
        (await MarketDataHttp.PricesOfAsync(fine.Id)).ShouldBeEmpty();
        (await MarketDataHttp.PricesOfAsync(none.Id)).ShouldBeEmpty();
    }

    [Test]
    public async Task AFailedCall_StoresNothing_KeepsTheLastRow_AndRecordsTheFailure()
    {
        var tenant = await TestApp.CreateTenantAsync("North Advisory", "north-advisory");
        var acme = await MarketDataHttp.AddInstrumentAsync(tenant.Id, "ACME.SIM", "USD");
        var first = new DateTimeOffset(2026, 11, 22, 1, 0, 0, TimeSpan.Zero);
        MarketDataHttp.Prices.Answer(MarketDataHttp.Item("ACME.SIM", "USD", 100m));
        await MarketDataHttp.RunAsync(first);

        MarketDataHttp.Prices.Fail(MarketDataFailure.Unavailable);
        var outcome = await MarketDataHttp.RunAsync(first.AddDays(1));

        outcome.Failed.ShouldBeTrue();
        outcome.Failure.ShouldBe(MarketDataFailure.Unavailable);
        outcome.Stored.ShouldBe(0);
        var row = (await MarketDataHttp.PricesOfAsync(acme.Id)).ShouldHaveSingleItem();
        row.AsOfDate.ShouldBe(new DateOnly(2026, 11, 22));
        row.Price.ShouldBe(100m);
        var feed = (await MarketDataHttp.FeedAsync()).ShouldNotBeNull();
        feed.LastSucceededAt.ShouldBe(first);
        feed.LastFailedAt.ShouldBe(first.AddDays(1));
        feed.LastFailure.ShouldBe(MarketDataFailure.Unavailable);
    }

    [Test]
    public async Task AFailedFirstCall_CreatesTheFeedWithOnlyTheFailure()
    {
        MarketDataHttp.Prices.Fail(MarketDataFailure.Unreachable);

        var outcome = await MarketDataHttp.RunAsync();

        outcome.Failure.ShouldBe(MarketDataFailure.Unreachable);
        var feed = (await MarketDataHttp.FeedAsync()).ShouldNotBeNull();
        feed.LastSucceededAt.ShouldBeNull();
        feed.LastFailedAt.ShouldNotBeNull();
        feed.LastFailure.ShouldBe(MarketDataFailure.Unreachable);
    }
}
