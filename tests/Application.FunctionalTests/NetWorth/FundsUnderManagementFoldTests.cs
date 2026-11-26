using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Valuations;
using MyWealthV2.Infrastructure.Data;
using static MyWealthV2.Application.FunctionalTests.NetWorth.FundsWorld;
using static MyWealthV2.Application.FunctionalTests.Valuations.ValuationHttp;

namespace MyWealthV2.Application.FunctionalTests.NetWorth;

/// <summary>Valuation-history R9–R12 and §1.1: the fold of each day of the series into the reporting currency.</summary>
[Parallelizable]
public class FundsUnderManagementFoldTests : TestBase
{
    private static readonly (int Day, decimal Nzd, decimal? Rate, decimal? Usd, decimal Total, bool Complete)[] AtTen =
    [
        (2, 41_550.00m, null, null, 41_550.00m, true),
        (3, 41_983.00m, null, null, 41_983.00m, true),
        (4, 44_133.32m, 1.7180m, 12_885.43m, 57_018.75m, true),
        (5, 44_434.00m, 1.7200m, 12_900.43m, 57_334.43m, true),
        (6, 42_790.45m, 1.7250m, 12_937.93m, 55_728.38m, true),
        (7, 42_790.45m, 1.7250m, 12_937.93m, 55_728.38m, true),
        (8, 42_804.72m, 1.7260m, 12_945.43m, 55_750.15m, true),
        (9, 43_092.48m, 1.7300m, 12_975.43m, 56_067.91m, true),
        (10, 42_930.09m, 1.7280m, 12_960.43m, 55_890.52m, true),
        (11, 34_750.72m, null, null, 34_750.72m, false)
    ];

    [Test]
    public async Task TheWorkedExample_At10_FoldsUsdAtTheRateOfEachDay()
    {
        var world = await CreateAsync();
        await world.AtTenAsync();

        var series = await FundsHttp.GetAsync(world.AdminToken, FundsHttp.Range(Day(2), Day(11)));

        foreach (var (day, nzd, rate, usd, total, complete) in AtTen)
        {
            var item = FundsHttp.Day(series, Day(day));
            var lines = item.GetProperty("currencies").EnumerateArray().ToList();
            AssertLine(lines[0], "NZD", nzd, "Folded", null, nzd);
            if (rate is { } usdRate)
            {
                lines.Count.ShouldBe(2);
                AssertLine(lines[1], "USD", 7_500.25m, "Folded", (usdRate, Day(day)), usd);
            }
            else
            {
                lines.Count.ShouldBe(1);
            }

            item.GetProperty("total").GetDecimal().ShouldBe(total, $"Total of {Day(day)}");
            item.GetProperty("complete").GetBoolean().ShouldBe(complete, $"Complete of {Day(day)}");
        }
    }

    [Test]
    public async Task TheWorkedExample_At10_AnswersTheBodyOfTheSpec()
    {
        var world = await CreateAsync();
        await world.AtTenAsync();

        var series = await FundsHttp.GetAsync(world.AdminToken, "?from=2026-03-10&to=2026-03-11");

        JsonSerializer.Serialize(series).ShouldBe(Compact("""
            {
              "reportingCurrency": "NZD",
              "from": "2026-03-10",
              "to": "2026-03-11",
              "days": [
                {
                  "date": "2026-03-10",
                  "currencies": [
                    { "currency": "NZD", "total": 42930.09, "status": "Folded", "fx": null, "value": 42930.09 },
                    { "currency": "USD", "total": 7500.25, "status": "Folded", "fx": { "rate": 1.728, "date": "2026-03-10", "source": "Frankfurter" }, "value": 12960.43 }
                  ],
                  "total": 55890.52,
                  "complete": true,
                  "accounts": 2,
                  "incompleteAccounts": 0,
                  "missingAccounts": 0
                },
                {
                  "date": "2026-03-11",
                  "currencies": [
                    { "currency": "NZD", "total": 34750.72, "status": "Folded", "fx": null, "value": 34750.72 }
                  ],
                  "total": 34750.72,
                  "complete": false,
                  "accounts": 2,
                  "incompleteAccounts": 1,
                  "missingAccounts": 1
                }
              ]
            }
            """));
    }

    [Test]
    public async Task TheWorkedExample_At14_FoldsTheUsdOfWednesdayAtItsRate()
    {
        var world = await CreateAsync();
        await world.AtTenAsync();
        await world.AtFourteenAsync();

        var series = await FundsHttp.GetAsync(world.AdminToken, FundsHttp.Range(Day(11), Day(11)));

        var wednesday = FundsHttp.Day(series, Day(11));
        AssertLine(wednesday.GetProperty("currencies")[1], "USD", 7_500.25m, "Folded", (1.7310m, Day(11)), 12_982.93m);
        wednesday.GetProperty("total").GetDecimal().ShouldBe(47_733.65m);
        wednesday.GetProperty("complete").GetBoolean().ShouldBeFalse();
    }

    [Test]
    public async Task CustomerB_SeesB1FoldedIntoTheReportingCurrency()
    {
        var world = await CreateAsync();
        await world.AtTenAsync();
        var customerB = await TestApp.IssueAccessTokenAsync(world.CustomerB);

        var series = await FundsHttp.GetAsync(customerB.AccessToken, FundsHttp.Range(Day(2), Day(4)));

        series.GetProperty("reportingCurrency").GetString().ShouldBe("NZD");
        var wednesday = FundsHttp.Day(series, Day(4));
        wednesday.GetProperty("total").GetDecimal().ShouldBe(12_885.43m);
        wednesday.GetProperty("complete").GetBoolean().ShouldBeTrue();
    }

    [Test]
    public async Task ACurrencyWithNoRate_OrARatePastTheAgeLimit_IsNotFolded_AndTheDayIsIncomplete()
    {
        var (tenant, _, customer, tokens) = await AccountHttp.SignInTenantAdmin();
        var token = tokens.AccessToken;
        var accountId = await AccountHttp.OpenAccountAsync(token, customer.PublicId, "Sydney", "Brokerage", "AUD");
        await PostAsync(token, Cash(accountId, "TransferIn", 1_000.00m, "2026-03-02T09:00:00Z"));
        await ValuationRunDb.MakeCustodialAsync(accountId, new DateTimeOffset(2026, 3, 2, 0, 0, 0, TimeSpan.Zero));
        await AddRateAsync("AUD", "NZD", Day(3), 1.1m);
        await ValuationRunDb.RunAsync(tenant.Id, Day(11));

        var series = await FundsHttp.GetAsync(token, FundsHttp.Range(Day(2), Day(11)));

        foreach (var day in Enumerable.Range(2, 10))
        {
            var item = FundsHttp.Day(series, Day(day));
            var line = item.GetProperty("currencies").EnumerateArray().Single();
            if (day == 2)
            {
                AssertLine(line, "AUD", 1_000.00m, "NoRate", null, null);
            }
            else if (day <= 7)
            {
                AssertLine(line, "AUD", 1_000.00m, "Folded", (1.1m, Day(3)), 1_100.00m);
            }
            else
            {
                AssertLine(line, "AUD", 1_000.00m, "StaleRate", (1.1m, Day(3)), null);
            }

            item.GetProperty("total").GetDecimal().ShouldBe(day is >= 3 and <= 7 ? 1_100.00m : 0m, $"Total of {Day(day)}");
            item.GetProperty("complete").GetBoolean().ShouldBe(day is >= 3 and <= 7, $"Complete of {Day(day)}");
            item.GetProperty("incompleteAccounts").GetInt32().ShouldBe(0);
            item.GetProperty("missingAccounts").GetInt32().ShouldBe(0);
        }

        var late = await FundsHttp.GetAsync(token, FundsHttp.Range(Day(10), Day(11)));
        AssertLine(FundsHttp.Day(late, Day(10)).GetProperty("currencies")[0], "AUD", 1_000.00m, "StaleRate", (1.1m, Day(3)), null);
    }

    [Test]
    public async Task TheReportingCurrency_IsReadWhenTheSeriesIsRead()
    {
        var world = await CreateAsync();
        await world.AtTenAsync();
        await SetReportingCurrencyAsync(world.Tenant.Id, "USD");
        var customerB = await TestApp.IssueAccessTokenAsync(world.CustomerB);

        var series = await FundsHttp.GetAsync(customerB.AccessToken, FundsHttp.Range(Day(10), Day(10)));

        series.GetProperty("reportingCurrency").GetString().ShouldBe("USD");
        var tuesday = FundsHttp.Day(series, Day(10));
        AssertLine(tuesday.GetProperty("currencies")[0], "USD", 7_500.25m, "Folded", null, 7_500.25m);
        tuesday.GetProperty("total").GetDecimal().ShouldBe(7_500.25m);
    }

    private static void AssertLine(JsonElement line, string currency, decimal total, string status, (decimal Rate, DateOnly Date)? fx, decimal? value)
    {
        line.GetProperty("currency").GetString().ShouldBe(currency);
        line.GetProperty("total").GetDecimal().ShouldBe(total);
        line.GetProperty("status").GetString().ShouldBe(status, $"{currency} {total}");
        if (fx is { } row)
        {
            line.GetProperty("fx").GetProperty("rate").GetDecimal().ShouldBe(row.Rate);
            line.GetProperty("fx").GetProperty("date").GetString().ShouldBe(row.Date.ToString("yyyy-MM-dd"));
            line.GetProperty("fx").GetProperty("source").GetString().ShouldBe("Frankfurter");
        }
        else
        {
            line.GetProperty("fx").ValueKind.ShouldBe(JsonValueKind.Null);
        }

        if (value is { } amount)
        {
            line.GetProperty("value").GetDecimal().ShouldBe(amount);
        }
        else
        {
            line.GetProperty("value").ValueKind.ShouldBe(JsonValueKind.Null);
        }
    }

    private static string Compact(string json)
    {
        using var document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement);
    }

    private static async Task SetReportingCurrencyAsync(int tenantId, string currency)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [Tenants] SET [ReportingCurrency] = {currency} WHERE [Id] = {tenantId}");
    }
}
