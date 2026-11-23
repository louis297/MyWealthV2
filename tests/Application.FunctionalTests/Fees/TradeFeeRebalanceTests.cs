using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Portfolios;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.ValueObjects;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Fees;

/// <summary>Trade fees R24–R29 (amended by rebalances R26): the planner's fees are estimates, and the buys are scaled to
/// leave cash for them.</summary>
[Parallelizable]
public class TradeFeeRebalanceTests : TestBase
{
    [Test]
    public async Task Assign_WithAFeeThatThePlainPlanCannotFund_ProposesAScaledBuyWithTheEstimatedFee()
    {
        var (tenant, _, customer, adviser) = await AccountHttp.SignInAdviser();
        var accountId = await AccountHttp.OpenAccountAsync(
            adviser.AccessToken, customer.PublicId, "Brokerage NZD", "Brokerage", "NZD", "Custodial");
        var instrumentId = await FeeHttp.CreateInstrumentAsync(adviser.AccessToken, "AIA", "NZD");
        await FeeHttp.SetPriceAsync(instrumentId, 10m, "NZD");
        (await CustodialHttp.FundAsync(adviser.AccessToken, accountId, 10000m)).StatusCode
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
        await FeeHttp.AddScheduleAsync(tenant.Id, "NZD", FeeHttp.Today, minimum: 50m, new FeeBand(0m, 0.5m));

        var response = await PortfolioHttp.Send(HttpMethod.Post, $"/accounts/{accountId}/portfolio", adviser.AccessToken, new
        {
            portfolioId
        });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        // Rebalances R9, R26: the planner's fees are estimates on the proposal; the trades are order fills later.
        json.RootElement.GetProperty("status").GetString().ShouldBe("Proposed");
        var item = await Rebalances.RebalanceHttp.GetAsync(adviser.AccessToken, json.RootElement.GetProperty("rebalanceId").GetGuid());
        var line = item.GetProperty("lines").EnumerateArray().Single();
        line.GetProperty("estimatedFee").GetDecimal().ShouldBe(50m);
        var gross = line.GetProperty("estimatedGross").GetDecimal();
        gross.ShouldBeLessThan(10000m);
        gross.ShouldBeGreaterThan(9900m);
        (gross + 50m).ShouldBeLessThanOrEqualTo(10000m);
        (await TradeFeeTradeTests.FeeDetailsAsync()).ShouldBeEmpty();
    }
}
