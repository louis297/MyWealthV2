using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Portfolios;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.ValueObjects;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Fees;

/// <summary>Trade fees R24–R29: rebalance trades carry fees, and the buys are scaled to leave cash for them.</summary>
public class TradeFeeRebalanceTests : TestBase
{
    [Test]
    public async Task Assign_WithAFeeThatThePlainPlanCannotFund_ScalesTheBuyAndPostsTheFee()
    {
        var (tenant, _, customer, adviser) = await AccountHttp.SignInAdviser();
        var accountId = await FeeHttp.OpenAccountAsync(adviser.AccessToken, customer.PublicId, "Brokerage", "NZD");
        var instrumentId = await FeeHttp.CreateInstrumentAsync(adviser.AccessToken, "AIA", "NZD");
        await FeeHttp.SetPriceAsync(instrumentId, 10m, "NZD");
        (await FeeHttp.PostCashAsync(adviser.AccessToken, accountId, 10000m, "2026-01-02T00:00:00Z")).StatusCode
            .ShouldBe(HttpStatusCode.Created);
        var portfolio = await PortfolioHttp.Send(HttpMethod.Post, "/portfolios", adviser.AccessToken, new
        {
            name = "Growth",
            baseCurrency = "NZD",
            cashResidualPercent = 0m,
            minimumTrade = 1m,
            lines = new[] { new { instrumentId, targetWeight = 100m } }
        });
        portfolio.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var portfolioJson = JsonDocument.Parse(await portfolio.Content.ReadAsStringAsync());
        var portfolioId = portfolioJson.RootElement.GetProperty("id").GetGuid();
        await AddScheduleStartingTodayAsync(tenant.Id, "NZD", minimum: 50m);

        var response = await PortfolioHttp.Send(HttpMethod.Post, $"/accounts/{accountId}/portfolio", adviser.AccessToken, new
        {
            portfolioId
        });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("status").GetString().ShouldBe("Success");
        var cash = await TradeFeeTradeTests.CashBalanceAsync(adviser.AccessToken, accountId);
        cash.ShouldBeGreaterThanOrEqualTo(0m);
        cash.ShouldBeLessThan(1m);
        var details = await TradeFeeTradeTests.FeeDetailsAsync();
        details.Count.ShouldBe(1);
        details[0].Fee.ShouldBe(50m);
        details[0].Basis.ShouldBeLessThan(10000m);
        details[0].Basis.ShouldBeGreaterThan(9900m);
    }

    /// <summary>A schedule from today (UTC) inserted directly: the API only accepts dates after the cut-off.</summary>
    private static async Task AddScheduleStartingTodayAsync(int tenantId, string currency, decimal minimum)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var catalogCurrency = await db.Set<Currency>().AsNoTracking().SingleAsync(row => row.Code == currency);
        var schedule = TradeFeeSchedule.Create(
            tenantId, catalogCurrency, FeeHttp.Today, minimum, null, [new FeeBand(0m, 0.5m)], FeeHttp.Today.AddDays(-1));
        schedule.CreatedBy = "test";
        db.TradeFeeSchedules.Add(schedule);
        await db.SaveChangesAsync();
    }
}
