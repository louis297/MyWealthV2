using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.AccountClosures;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Interest;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Valuations;
using static MyWealthV2.Application.FunctionalTests.Valuations.ValuationRunDb;
using static MyWealthV2.Application.FunctionalTests.Valuations.ValuationWorld;

namespace MyWealthV2.Application.FunctionalTests.Valuations;

/// <summary>
/// Valuation run R2–R13: the worked example of valuation-run §1.1 as a sequence of runs through the dates the daily job
/// would have used, then the days of an account, an empty day and a failure. The runs are called as the job calls them,
/// with the through date of the example; every March 2026 day is long final on the real clock.
/// </summary>
[Parallelizable]
public class ValuationRunTests : TestBase
{
    private static readonly DateTimeOffset Opened = new(2026, 2, 27, 9, 0, 0, TimeSpan.Zero);

    private static readonly (int Day, decimal Total)[] Totals =
    [
        (2, 41_550.00m), (3, 41_983.00m), (4, 44_133.32m), (5, 44_434.00m), (6, 42_790.45m), (7, 42_790.45m),
        (8, 42_804.72m), (9, 43_092.48m), (10, 42_930.09m), (11, 34_750.72m)
    ];

    [Test]
    public async Task TheWorkedExample_StoresEachDayOnce_AsBooked_AndALaterPriceChangesNothing()
    {
        var world = await CreateAsync();
        await MakeCustodialAsync(world.AccountId, Opened);
        var tenantId = world.Tenant.Id;

        // Earlier runs: the days from the day it was opened, before any price is stored, are incomplete (R16).
        var first = await RunAsync(tenantId, Day(1));
        (first.AccountsProcessed, first.AccountsFailed, first.DaysValued, first.DaysIncomplete).ShouldBe((1, 0, 3, 3));
        (await ValuedThroughAsync(world.AccountId)).ShouldBe(Day(1));

        var wednesday = await RunAsync(tenantId, Day(2));
        (wednesday.DaysValued, wednesday.DaysIncomplete).ShouldBe((1, 0));

        // Thursday 5: worker down. Friday 6 catches up Tuesday 3 and Wednesday 4 (R6).
        var friday = await RunAsync(tenantId, Day(4));
        (friday.AccountsProcessed, friday.AccountsFailed, friday.DaysValued, friday.DaysIncomplete).ShouldBe((1, 0, 2, 0));
        friday.ThroughDate.ShouldBe(Day(4));
        friday.Trigger.ShouldBe(ValuationRunTrigger.Scheduled);
        friday.RanByUserId.ShouldBeNull();
        friday.CompletedAt.ShouldNotBeNull();

        await RunAsync(tenantId, Day(5));
        await RunAsync(tenantId, Day(10));
        var manual = await RunAsync(tenantId, Day(11), ValuationRunTrigger.Manual, world.Adviser.Id);
        (manual.DaysValued, manual.DaysIncomplete).ShouldBe((1, 1));

        var stored = await StoredAsync(world.AccountId);
        stored.Select(row => row.Date).ShouldBe(Days(new DateOnly(2026, 2, 27), Day(11)));
        foreach (var (day, total) in Totals)
        {
            stored.Single(row => row.Date == Day(day)).Total.ShouldBe(total, $"Total of {Day(day)}");
        }

        stored.Where(row => row.Date >= Day(2)).Select(row => row.Complete).ShouldBe(Totals.Select(row => row.Day != 11));
        stored.Single(row => row.Date == Day(4)).ValuationRunId.ShouldBe(friday.Id);
        stored.Single(row => row.Date == Day(3)).ValuationRunId.ShouldBe(friday.Id);
        (await ValuedThroughAsync(world.AccountId)).ShouldBe(Day(11));

        // Wednesday 11: stored with its mark and the CCC line with the price row of Friday 6 (R9).
        var eleventh = stored.Single(row => row.Date == Day(11));
        (eleventh.Complete, eleventh.UnvaluedCount, eleventh.ValuationRunId).ShouldBe((false, 1, manual.Id));
        var cccId = await InstrumentIdAsync(world.Ccc);
        var ccc = eleventh.Lines.Single(line => line.InstrumentId == cccId);
        (ccc.Status, ccc.PriceAmount, ccc.PriceDate, ccc.Value).ShouldBe((ValuationLineStatus.StalePrice, (decimal?)41.30m, (DateOnly?)Day(6), (decimal?)null));

        // A repeat stores nothing twice and stores a run with zero counts (R7).
        var repeat = await RunAsync(tenantId, Day(11));
        (repeat.AccountsProcessed, repeat.AccountsFailed, repeat.DaysValued, repeat.DaysIncomplete).ShouldBe((0, 0, 0, 0));
        (await StoredAsync(world.AccountId)).Count.ShouldBe(stored.Count);

        // A price inserted later changes the read, never the stored day (R10).
        await ValuationHttp.AddPriceAsync(world.Ccc, "USD", Day(9), 41.40m);
        await ValuationHttp.AddPriceAsync(world.Ccc, "USD", Day(10), 41.45m);
        await ValuationHttp.AddPriceAsync(world.Ccc, "USD", Day(11), 41.50m);
        var read = await ValuationHttp.ReadAsync(world.Token, world.AccountId, Day(11));
        (read.GetProperty("total").GetDecimal(), read.GetProperty("complete").GetBoolean()).ShouldBe((43_371.10m, true));
        var after = await StoredAsync(world.AccountId);
        after.Single(row => row.Date == Day(11)).ShouldSatisfyAllConditions(
            row => row.Total.ShouldBe(34_750.72m),
            row => row.Complete.ShouldBeFalse());
        after.Single(row => row.Date == Day(9)).Total.ShouldBe(43_092.48m);
        after.Single(row => row.Date == Day(10)).Total.ShouldBe(42_930.09m);
    }

    [Test]
    public async Task AStoredDay_EqualsTheValuationReadOfThatDay_WhenItIsStored()
    {
        var world = await CreateAsync();
        await MakeCustodialAsync(world.AccountId, Opened);

        await RunAsync(world.Tenant.Id, Day(11));

        var stored = await StoredAsync(world.AccountId);
        foreach (var day in Days(Day(2), Day(11)))
        {
            var row = stored.Single(item => item.Date == day);
            var read = await ValuationHttp.ReadAsync(world.Token, world.AccountId, day);
            (row.Currency, row.Cash, row.HoldingsValue, row.Total, row.Complete, row.UnvaluedCount).ShouldBe((
                read.GetProperty("currency").GetString()!,
                read.GetProperty("cash").GetDecimal(),
                read.GetProperty("holdingsValue").GetDecimal(),
                read.GetProperty("total").GetDecimal(),
                read.GetProperty("complete").GetBoolean(),
                read.GetProperty("unvaluedCount").GetInt32()), $"Valuation of {day}");
            var holdings = read.GetProperty("holdings").EnumerateArray().ToList();
            row.Lines.Count.ShouldBe(holdings.Count);
            foreach (var holding in holdings)
            {
                var instrumentId = await InstrumentIdAsync(holding.GetProperty("instrumentId").GetGuid());
                var line = row.Lines.Single(item => item.InstrumentId == instrumentId);
                (line.Quantity, line.LedgerQuantity, line.Status.ToString(), line.MarketValue, line.Value).ShouldBe((
                    holding.GetProperty("quantity").GetDecimal(),
                    holding.GetProperty("ledgerQuantity").GetDecimal(),
                    holding.GetProperty("status").GetString()!,
                    Decimal(holding.GetProperty("marketValue")),
                    Decimal(holding.GetProperty("value"))));
                var price = holding.GetProperty("price");
                line.PriceAmount.ShouldBe(price.ValueKind == JsonValueKind.Null ? null : price.GetProperty("amount").GetDecimal());
                var fx = holding.GetProperty("fx");
                line.FxRate.ShouldBe(fx.ValueKind == JsonValueKind.Null ? null : fx.GetProperty("rate").GetDecimal());
                line.FxSource.ShouldBe(fx.ValueKind == JsonValueKind.Null ? null : fx.GetProperty("source").GetString());
            }
        }
    }

    [Test]
    public async Task AnAccount_IsValuedFromTheDayItWasOpened_EmptyDaysToo()
    {
        var (tenant, _, customer, tokens) = await AccountHttp.SignInTenantAdmin();
        var accountId = await InterestHttp.OpenCustodialAsync(tenant.Id, tokens.AccessToken, customer.PublicId);
        var opened = LastFinalDay.AddDays(-3);
        await SetCreatedAsync(accountId, opened);

        var run = await RunAsync(tenant.Id, LastFinalDay);

        (run.AccountsProcessed, run.DaysValued, run.DaysIncomplete).ShouldBe((1, 4, 0));
        var stored = await StoredAsync(accountId);
        stored.Select(row => row.Date).ShouldBe(Days(opened, LastFinalDay));
        stored.ShouldAllBe(row => row.Cash == 0m && row.Total == 0m && row.Complete && row.UnvaluedCount == 0 && row.Lines.Count == 0);
        stored.ShouldAllBe(row => row.Currency == "USD");
        (await ValuedThroughAsync(accountId)).ShouldBe(LastFinalDay);
    }

    [Test]
    public async Task AClosedAccount_StopsAtTheDayItsClosureCompleted_AndAReopenedOneGoesOn()
    {
        var (tenant, _, customer, tokens) = await AccountHttp.SignInTenantAdmin();
        var token = tokens.AccessToken;
        var accountId = await InterestHttp.OpenCustodialAsync(tenant.Id, token, customer.PublicId);
        await AccountClosureHttp.RequestAsync(token, accountId);
        var opened = LastFinalDay.AddDays(-8);
        var closed = LastFinalDay.AddDays(-4);
        await SetCreatedAsync(accountId, opened);
        await SetClosureCompletedAsync(accountId, closed);

        var run = await RunAsync(tenant.Id, LastFinalDay);

        run.DaysValued.ShouldBe(5);
        (await StoredAsync(accountId)).Select(row => row.Date).ShouldBe(Days(opened, closed));
        (await ValuedThroughAsync(accountId)).ShouldBe(closed);
        (await RunAsync(tenant.Id, LastFinalDay)).AccountsProcessed.ShouldBe(0);

        await AccountClosureHttp.ReopenAsync(token, accountId);
        var again = await RunAsync(tenant.Id, LastFinalDay);

        again.DaysValued.ShouldBe(4);
        (await StoredAsync(accountId)).Select(row => row.Date).ShouldBe(Days(opened, LastFinalDay));
        (await ValuedThroughAsync(accountId)).ShouldBe(LastFinalDay);
    }

    [Test]
    public async Task AnExternalAccount_IsNeverStored()
    {
        var world = await CreateAsync();

        var run = await RunAsync(world.Tenant.Id, Day(11));

        (run.AccountsProcessed, run.DaysValued).ShouldBe((0, 0));
        (await StoredAsync(world.AccountId)).ShouldBeEmpty();
        (await ValuedThroughAsync(world.AccountId)).ShouldBeNull();
    }

    [Test]
    public async Task AFailure_StopsThatAccountAtTheFailedDay_TheOthersGoOn_AndTheNextRunStartsThere()
    {
        var (tenant, _, customer, tokens) = await AccountHttp.SignInTenantAdmin();
        var failing = await InterestHttp.OpenCustodialAsync(tenant.Id, tokens.AccessToken, customer.PublicId);
        var healthy = await InterestHttp.OpenCustodialAsync(tenant.Id, tokens.AccessToken, customer.PublicId);
        var opened = LastFinalDay.AddDays(-2);
        await SetCreatedAsync(failing, opened);
        await SetCreatedAsync(healthy, opened);

        var run = await RunFailingAsync(tenant.Id, LastFinalDay, failing);

        (run.AccountsProcessed, run.AccountsFailed, run.DaysValued).ShouldBe((2, 1, 3));
        run.CompletedAt.ShouldNotBeNull();
        (await StoredAsync(failing)).ShouldBeEmpty();
        (await ValuedThroughAsync(failing)).ShouldBeNull();
        (await StoredAsync(healthy)).Count.ShouldBe(3);

        var next = await RunAsync(tenant.Id, LastFinalDay);

        (next.AccountsProcessed, next.AccountsFailed, next.DaysValued).ShouldBe((1, 0, 3));
        (await StoredAsync(failing)).Select(row => row.Date).ShouldBe(Days(opened, LastFinalDay));
    }

    private static decimal? Decimal(JsonElement element) => element.ValueKind == JsonValueKind.Null ? null : element.GetDecimal();
}
