using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.AccountClosures;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Interest;
using MyWealthV2.Application.FunctionalTests.Valuations;
using static MyWealthV2.Application.FunctionalTests.NetWorth.FundsWorld;

namespace MyWealthV2.Application.FunctionalTests.NetWorth;

/// <summary>Valuation-history R4–R8 and §1.1: what a day of the series counts, the expected accounts and completeness.</summary>
[Parallelizable]
public class FundsUnderManagementTests : TestBase
{
    private static readonly string Example = FundsHttp.Range(Day(2), Day(11));

    private static readonly (int Day, decimal Nzd)[] A1Totals =
    [
        (2, 41_550.00m), (3, 41_983.00m), (4, 44_133.32m), (5, 44_434.00m), (6, 42_790.45m), (7, 42_790.45m),
        (8, 42_804.72m), (9, 43_092.48m), (10, 42_930.09m), (11, 34_750.72m)
    ];

    [Test]
    public async Task TheWorkedExample_AsTheTenantAdmin_At10_SumsEachCurrency_AndCountsTheIncompleteAndTheMissingAccount()
    {
        var world = await CreateAsync();
        await world.AtTenAsync();

        var series = await FundsHttp.GetAsync(world.AdminToken, Example);

        series.GetProperty("reportingCurrency").GetString().ShouldBe("NZD");
        FundsHttp.Dates(series).Count.ShouldBe(10);
        foreach (var (day, nzd) in A1Totals)
        {
            var item = FundsHttp.Day(series, Day(day));
            var expected = new List<(string, decimal)> { ("NZD", nzd) };
            if (day is >= 4 and <= 10)
            {
                expected.Add(("USD", 7_500.25m));
            }

            Lines(item).ShouldBe(expected, $"Currencies of {Day(day)}");
            Counts(item).ShouldBe(day >= 4 ? (2, day == 11 ? 1 : 0, day == 11 ? 1 : 0) : (1, 0, 0), $"Counts of {Day(day)}");
        }

        foreach (var day in new[] { 2, 3 })
        {
            FundsHttp.Day(series, Day(day)).GetProperty("complete").GetBoolean().ShouldBeTrue();
        }

        var wednesday = FundsHttp.Day(series, Day(11));
        wednesday.GetProperty("complete").GetBoolean().ShouldBeFalse();
        wednesday.GetProperty("total").GetDecimal().ShouldBe(34_750.72m);
    }

    [Test]
    public async Task TheWorkedExample_At14_CountsTheAccountStoredSince_AndWednesdayStaysIncomplete()
    {
        var world = await CreateAsync();
        await world.AtTenAsync();
        await world.AtFourteenAsync();

        var series = await FundsHttp.GetAsync(world.AdminToken, Example);

        var wednesday = FundsHttp.Day(series, Day(11));
        Lines(wednesday).ShouldBe([("NZD", 34_750.72m), ("USD", 7_500.25m)]);
        Counts(wednesday).ShouldBe((2, 1, 0));
        wednesday.GetProperty("complete").GetBoolean().ShouldBeFalse();
        Lines(FundsHttp.Day(series, Day(10))).ShouldBe([("NZD", 42_930.09m), ("USD", 7_500.25m)]);
    }

    [Test]
    public async Task TheWorkedExample_AsAdviserAna_CountsOnlyA1()
    {
        var world = await CreateAsync();
        await world.AtTenAsync();
        var ana = await TestApp.IssueAccessTokenAsync(world.A.Adviser);

        var series = await FundsHttp.GetAsync(ana.AccessToken, Example);

        foreach (var (day, nzd) in A1Totals)
        {
            var item = FundsHttp.Day(series, Day(day));
            Lines(item).ShouldBe([("NZD", nzd)], $"Currencies of {Day(day)}");
            item.GetProperty("total").GetDecimal().ShouldBe(nzd);
            Counts(item).ShouldBe((1, day == 11 ? 1 : 0, 0), $"Counts of {Day(day)}");
            item.GetProperty("complete").GetBoolean().ShouldBe(day != 11, $"Complete of {Day(day)}");
        }
    }

    [Test]
    public async Task TheWorkedExample_AsCustomerB_CountsOnlyB1_FromItsFirstDay()
    {
        var world = await CreateAsync();
        await world.AtTenAsync();
        await world.AtFourteenAsync();
        var customerB = await TestApp.IssueAccessTokenAsync(world.CustomerB);

        var series = await FundsHttp.GetAsync(customerB.AccessToken, Example);

        foreach (var day in new[] { 2, 3 })
        {
            var item = FundsHttp.Day(series, Day(day));
            Lines(item).ShouldBeEmpty();
            item.GetProperty("total").GetDecimal().ShouldBe(0m);
            Counts(item).ShouldBe((0, 0, 0));
            item.GetProperty("complete").GetBoolean().ShouldBeTrue();
        }

        foreach (var day in Enumerable.Range(4, 8))
        {
            var item = FundsHttp.Day(series, Day(day));
            Lines(item).ShouldBe([("USD", 7_500.25m)], $"Currencies of {Day(day)}");
            Counts(item).ShouldBe((1, 0, 0), $"Counts of {Day(day)}");
        }
    }

    [Test]
    public async Task AClosedAccount_IsNotExpectedAfterItsClosureDay_AndAReopenedOneIsAgain()
    {
        var (tenant, _, customer, tokens) = await AccountHttp.SignInTenantAdmin();
        var token = tokens.AccessToken;
        var accountId = await InterestHttp.OpenCustodialAsync(tenant.Id, token, customer.PublicId, "NZD");
        await AccountClosureHttp.RequestAsync(token, accountId);
        var last = ValuationRunDb.LastFinalDay;
        var opened = last.AddDays(-8);
        var closed = last.AddDays(-4);
        await ValuationRunDb.SetCreatedAsync(accountId, opened);
        await ValuationRunDb.SetClosureCompletedAsync(accountId, closed);
        var range = FundsHttp.Range(opened.AddDays(-1), last);

        var beforeTheRun = await FundsHttp.GetAsync(token, range);
        await ValuationRunDb.RunAsync(tenant.Id, last);
        var closedSeries = await FundsHttp.GetAsync(token, range);
        await AccountClosureHttp.ReopenAsync(token, accountId);
        var reopenedBeforeTheRun = await FundsHttp.GetAsync(token, range);
        await ValuationRunDb.RunAsync(tenant.Id, last);
        var reopened = await FundsHttp.GetAsync(token, range);

        foreach (var day in ValuationRunDb.Days(opened.AddDays(-1), last))
        {
            var expectedWhileClosed = day >= opened && day <= closed ? 1 : 0;
            var expectedReopened = day >= opened ? 1 : 0;
            Counts(FundsHttp.Day(beforeTheRun, day)).ShouldBe((expectedWhileClosed, 0, expectedWhileClosed), $"Before the run, {day}");
            FundsHttp.Day(beforeTheRun, day).GetProperty("complete").GetBoolean().ShouldBe(expectedWhileClosed == 0, $"Before the run, {day}");
            Counts(FundsHttp.Day(closedSeries, day)).ShouldBe((expectedWhileClosed, 0, 0), $"Closed, {day}");
            FundsHttp.Day(closedSeries, day).GetProperty("complete").GetBoolean().ShouldBeTrue($"Closed, {day}");
            Counts(FundsHttp.Day(reopenedBeforeTheRun, day)).ShouldBe(
                (expectedReopened, 0, day > closed ? 1 : 0), $"Reopened before the run, {day}");
            Counts(FundsHttp.Day(reopened, day)).ShouldBe((expectedReopened, 0, 0), $"Reopened, {day}");
            FundsHttp.Day(reopened, day).GetProperty("complete").GetBoolean().ShouldBeTrue($"Reopened, {day}");
        }

        Lines(FundsHttp.Day(reopened, last)).ShouldBe([("NZD", 0m)]);
    }

    [Test]
    public async Task AnExternalAccount_IsNeverCounted()
    {
        var world = await ValuationWorld.CreateAsync();
        await ValuationRunDb.RunAsync(world.Tenant.Id, Day(11));

        var series = await FundsHttp.GetAsync(world.Token, Example);

        foreach (var day in series.GetProperty("days").EnumerateArray())
        {
            day.GetProperty("currencies").GetArrayLength().ShouldBe(0);
            Counts(day).ShouldBe((0, 0, 0));
        }
    }

    private static List<(string, decimal)> Lines(JsonElement day) =>
        day.GetProperty("currencies").EnumerateArray()
            .Select(line => (line.GetProperty("currency").GetString()!, line.GetProperty("total").GetDecimal()))
            .ToList();

    private static (int Accounts, int Incomplete, int Missing) Counts(JsonElement day) => (
        day.GetProperty("accounts").GetInt32(),
        day.GetProperty("incompleteAccounts").GetInt32(),
        day.GetProperty("missingAccounts").GetInt32());
}
