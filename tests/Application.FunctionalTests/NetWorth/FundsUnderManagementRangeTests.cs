using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Valuations;

namespace MyWealthV2.Application.FunctionalTests.NetWorth;

/// <summary>Valuation-history R3, R8: the range of the series, its defaults and its refusals.</summary>
[Parallelizable]
public class FundsUnderManagementRangeTests : TestBase
{
    private static DateOnly LastFinalDay => ValuationRunDb.LastFinalDay;

    [Test]
    public async Task ByDefault_TheSeriesIsTheNinetyDaysToTheLastFinalDay_EveryDayListedOldestFirst()
    {
        var (_, _, _, tokens) = await AccountHttp.SignInTenantAdmin();

        var series = await FundsHttp.GetAsync(tokens.AccessToken);

        series.GetProperty("reportingCurrency").GetString().ShouldBe("NZD");
        series.GetProperty("to").GetString().ShouldBe(LastFinalDay.ToString("yyyy-MM-dd"));
        series.GetProperty("from").GetString().ShouldBe(LastFinalDay.AddDays(-89).ToString("yyyy-MM-dd"));
        FundsHttp.Dates(series).ShouldBe(ValuationRunDb.Days(LastFinalDay.AddDays(-89), LastFinalDay).Select(day => day.ToString("yyyy-MM-dd")).ToList());
        foreach (var day in series.GetProperty("days").EnumerateArray())
        {
            day.GetProperty("currencies").GetArrayLength().ShouldBe(0);
            day.GetProperty("total").GetDecimal().ShouldBe(0m);
            day.GetProperty("complete").GetBoolean().ShouldBeTrue();
            day.GetProperty("accounts").GetInt32().ShouldBe(0);
            day.GetProperty("incompleteAccounts").GetInt32().ShouldBe(0);
            day.GetProperty("missingAccounts").GetInt32().ShouldBe(0);
        }
    }

    [Test]
    public async Task ToAlone_StartsEightyNineDaysBefore_AndFromAlone_EndsOnTheLastFinalDay()
    {
        var (_, _, _, tokens) = await AccountHttp.SignInTenantAdmin();

        var toAlone = await FundsHttp.GetAsync(tokens.AccessToken, "?to=2026-03-11");
        toAlone.GetProperty("from").GetString().ShouldBe("2025-12-12");
        toAlone.GetProperty("days").GetArrayLength().ShouldBe(90);

        var from = LastFinalDay.AddDays(-2);
        var fromAlone = await FundsHttp.GetAsync(tokens.AccessToken, $"?from={from:yyyy-MM-dd}");
        fromAlone.GetProperty("to").GetString().ShouldBe(LastFinalDay.ToString("yyyy-MM-dd"));
        fromAlone.GetProperty("days").GetArrayLength().ShouldBe(3);
    }

    [Test]
    public async Task ARangeOf366Days_IsRead()
    {
        var (_, _, _, tokens) = await AccountHttp.SignInTenantAdmin();

        var series = await FundsHttp.GetAsync(tokens.AccessToken, FundsHttp.Range(new DateOnly(2025, 3, 11), new DateOnly(2026, 3, 11)));

        series.GetProperty("days").GetArrayLength().ShouldBe(366);
    }

    [TestCase("?from=2026-3-1", "From", "From must be a date as yyyy-MM-dd.")]
    [TestCase("?to=11/03/2026", "To", "To must be a date as yyyy-MM-dd.")]
    [TestCase("?from=2026-03-12&to=2026-03-11", "From", "From must not be after To.")]
    [TestCase("?from=2025-03-10&to=2026-03-11", "From", "A series covers at most 366 days.")]
    public async Task ARangeThatIsNotAllowed_Is400(string query, string key, string message)
    {
        var (_, _, _, tokens) = await AccountHttp.SignInTenantAdmin();

        var errors = await CustodialHttp.ErrorsAsync(await FundsHttp.Send(tokens.AccessToken, query));

        errors.Keys.ShouldBe([key]);
        errors[key].ShouldBe([message]);
    }

    [Test]
    public async Task ToAfterTheLastFinalDay_Is400()
    {
        var (_, _, _, tokens) = await AccountHttp.SignInTenantAdmin();

        var errors = await CustodialHttp.ErrorsAsync(await FundsHttp.Send(tokens.AccessToken, $"?to={LastFinalDay.AddDays(1):yyyy-MM-dd}"));

        errors.Keys.ShouldBe(["To"]);
        errors["To"].ShouldBe(["Funds under management can be read only up to the day before yesterday."]);
    }
}
