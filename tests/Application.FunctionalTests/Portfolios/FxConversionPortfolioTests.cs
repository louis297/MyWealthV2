using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Transactions;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Portfolios;

/// <summary>FX conversions R24: cash-only, allowed on an assigned Account on either side, no automatic rebalance.</summary>
[Parallelizable]
public class FxConversionPortfolioTests : TestBase
{
    [Test]
    public async Task ConversionToAndFromAnAssignedAccount_IsAllowedAndEnqueuesNoRebalance()
    {
        var (_, _, customer, tokens) = await Accounts.AccountHttp.SignInAdviser(
            "Convert Firm", "convert-firm", "ann@convert-firm", customerEmail: "ned@convert-firm");
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken);
        // Custody modes R8, R14: both accounts are Custodial, so the portfolio can be assigned and they can trade.
        var brokerage = await CustodialHttp.OpenCustodialAsync(tokens.AccessToken, customer.PublicId, "Model", "NZD");
        var usdBank = await CustodialHttp.OpenCustodialAsync(tokens.AccessToken, customer.PublicId, "USD brokerage", "USD");
        (await Custody.CustodialHttp.FundAsync(tokens.AccessToken, usdBank, 1000m))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        // A Custodial conversion is booked at the server clock (custody modes R10), so the rate is today's.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await FxConversionTests.SetRateAsync("USD", "NZD", 1.70000000m, today);
        await FxConversionTests.SetRateAsync("NZD", "USD", FxConversionTests.NzdUsd, today);
        await AssignPortfolioAsync(tokens.AccessToken, brokerage, instrumentId);
        var scheduler = FunctionalTestSetup.WebApiServices.GetRequiredService<RecordingRebalanceScheduler>();
        var before = scheduler.Enqueued.Count;

        await LegAccountsFixture.ReadIdAsync(await FxConversionTests.PostAsync(
            tokens.AccessToken,
            FxConversionTests.SellBody(usdBank, brokerage, 600m, expectedRate: 1.70000000m).WithoutBookedAt()));
        var outId = await LegAccountsFixture.ReadIdAsync(await FxConversionTests.PostAsync(
            tokens.AccessToken, FxConversionTests.SellBody(brokerage, usdBank, 100m).WithoutBookedAt()));
        await LegAccountsFixture.ReadIdAsync(await LegAccountsFixture.ReverseAsync(tokens.AccessToken, outId));

        (await InternalTransferTests.CashBalanceAsync(tokens.AccessToken, brokerage)).ShouldBe(1020m);
        (await InternalTransferTests.CashBalanceAsync(tokens.AccessToken, usdBank)).ShouldBe(400m);
        scheduler.Enqueued.Count.ShouldBe(before);
    }

    private static async Task<Guid> CreateInstrumentAsync(string accessToken)
    {
        var instrument = await Instruments.InstrumentHttp.Send(HttpMethod.Post, "/instruments", accessToken, new
        {
            symbol = "AIA",
            name = "AIA fund",
            quoteCurrency = "NZD"
        });
        instrument.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var json = JsonDocument.Parse(await instrument.Content.ReadAsStringAsync());
        var instrumentId = json.RootElement.GetProperty("id").GetGuid();
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await TestPrices.UpsertAsync(db, instrumentId, 10m, "NZD");
        return instrumentId;
    }

    private static async Task AssignPortfolioAsync(string accessToken, Guid accountId, Guid instrumentId)
    {
        var created = await PortfolioHttp.Send(HttpMethod.Post, "/portfolios", accessToken, new
        {
            name = "Growth",
            baseCurrency = "NZD",
            cashResidualPercent = 0m,
            minimumTrade = 1m,
            lines = new[] { new { instrumentId, targetWeight = 100m } }
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var portfolioId = json.RootElement.GetProperty("id").GetGuid();
        (await PortfolioHttp.Send(HttpMethod.Post, $"/accounts/{accountId}/portfolio", accessToken, new { portfolioId }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
