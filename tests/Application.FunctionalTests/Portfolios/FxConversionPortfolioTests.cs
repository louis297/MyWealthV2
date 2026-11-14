using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Transactions;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Portfolios;

/// <summary>FX conversions R24: cash-only, allowed on an assigned Account on either side, no automatic rebalance.</summary>
public class FxConversionPortfolioTests : TestBase
{
    [Test]
    public async Task ConversionToAndFromAnAssignedAccount_IsAllowedAndEnqueuesNoRebalance()
    {
        var (_, _, customer, tokens) = await Accounts.AccountHttp.SignInAdviser(
            "Convert Firm", "convert-firm", "ann@convert-firm", customerEmail: "ned@convert-firm");
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken);
        var brokerage = await InternalTransferTests.OpenAccountAsync(
            tokens.AccessToken, customer.PublicId, "Model", type: "Brokerage");
        var usdBank = await InternalTransferTests.OpenAccountAsync(tokens.AccessToken, customer.PublicId, "USD bank", "USD");
        (await TransactionHttp.Send(HttpMethod.Post, "/transactions", tokens.AccessToken, new
            {
                accountId = usdBank,
                type = "TransferIn",
                amount = 1000m,
                bookedAt = "2026-11-10T08:00:00Z"
            }, Guid.NewGuid()))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        await FxConversionTests.SetRateAsync("USD", "NZD", 1.70000000m, FxConversionTests.RateAsOf);
        await FxConversionTests.SetRateAsync("NZD", "USD", FxConversionTests.NzdUsd, FxConversionTests.RateAsOf);
        await AssignPortfolioAsync(tokens.AccessToken, brokerage, instrumentId);
        var scheduler = FunctionalTestSetup.WebApiServices.GetRequiredService<RecordingRebalanceScheduler>();
        var before = scheduler.Enqueued.Count;

        await LegAccountsFixture.ReadIdAsync(await FxConversionTests.PostAsync(
            tokens.AccessToken,
            FxConversionTests.SellBody(usdBank, brokerage, 600m, expectedRate: 1.70000000m)));
        var outId = await LegAccountsFixture.ReadIdAsync(await FxConversionTests.PostAsync(
            tokens.AccessToken, FxConversionTests.SellBody(brokerage, usdBank, 100m)));
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
