using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.MarketData;
using MyWealthV2.Application.FunctionalTests.NominatedBankAccounts;
using MyWealthV2.Application.MarketData;
using MyWealthV2.Domain.Enums;
using static MyWealthV2.Application.FunctionalTests.CorporateActions.CorporateActionFeedHttp;
using static MyWealthV2.Application.FunctionalTests.CorporateActions.CorporateActionHttp;

namespace MyWealthV2.Application.FunctionalTests.CorporateActions;

/// <summary>Corporate-actions R8: one run of a market data connection pulls, processes and applies its corporate
/// actions before it reads prices, and skips the price read when the pull failed.</summary>
[Parallelizable]
public class SyncMarketDataConnectionTests : TestBase
{
    [Test]
    public async Task AnEffectiveItem_IsAppliedToEveryHolder_InTheSameRun_AndThePricesAreReadAfter()
    {
        var world = await NominatedWorld.CreateAsync();
        await HoldersAsync(world, "ACME.SIM");
        Feed.Add(Connection, Item(status: PartyActionStatus.Effective));
        MarketDataHttp.Prices.Answer(MarketDataHttp.Item("ACME.SIM", "USD", 50m));

        var outcome = await SyncAsync();

        outcome.Failed.ShouldBeFalse();
        (outcome.Pull.Stored, outcome.Processing.Processed, outcome.Apply.Posted).ShouldBe((1, 1, 2));
        outcome.Prices.ShouldNotBeNull().Stored.ShouldBe(1);
        MarketDataHttp.Prices.Requests.ShouldBe([Connection]);
        var action = (await CorporateActionFeedHttp.ActionsOfAsync(world.Tenant.Id)).Single();
        action.Status.ShouldBe(CorporateActionStatus.Applied);
        (await EntitlementsAsync(action.PublicId)).Select(row => row.QuantityAfter).ShouldBe([200m, 20m]);
    }

    [Test]
    public async Task AFailedPull_KeepsTheCursor_StillAppliesStoredAnnouncements_AndReadsNoPrices()
    {
        var world = await NominatedWorld.CreateAsync();
        await HoldersAsync(world, "ACME.SIM");
        Feed.Add(Connection, Item(status: PartyActionStatus.Effective));
        (await PullAsync()).Stored.ShouldBe(1);
        Feed.Add(Connection, Item("w-CA2"));
        Feed.Fail(Connection, MarketDataFailure.Unavailable);

        var outcome = await SyncAsync();

        outcome.Failed.ShouldBeTrue();
        outcome.Pull.Failure.ShouldBe(MarketDataFailure.Unavailable);
        (outcome.Processing.Processed, outcome.Apply.Posted).ShouldBe((1, 2));
        outcome.Prices.ShouldBeNull();
        MarketDataHttp.Prices.Requests.ShouldBeEmpty();
        (await FeedStateAsync()).ShouldNotBeNull().Cursor.ShouldBe("1");
        (await CorporateActionFeedHttp.ActionsOfAsync(world.Tenant.Id)).Single().Status.ShouldBe(CorporateActionStatus.Applied);
    }

    [Test]
    public async Task AFailedPriceRead_FailsTheRun_AfterTheCorporateActions()
    {
        var tenant = await TestApp.CreateTenantAsync("North", "north-sync");
        await MarketDataHttp.AddInstrumentAsync(tenant.Id, "ACME.SIM", "USD");
        Feed.Add(Connection, Item());
        MarketDataHttp.Prices.Fail(MarketDataFailure.Timeout);

        var outcome = await SyncAsync();

        outcome.Failed.ShouldBeTrue();
        outcome.Processing.Processed.ShouldBe(1);
        outcome.Prices.ShouldNotBeNull().Failure.ShouldBe(MarketDataFailure.Timeout);
    }

    private static async Task<MarketDataSyncOutcome> SyncAsync()
    {
        await using var scope = FunctionalTestSetup.WebApiServices.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SyncMarketDataConnection>().RunAsync(Connection);
    }
}
