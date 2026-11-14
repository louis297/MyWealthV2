using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Transactions;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Portfolios;

/// <summary>Internal transfers R16: cash-only, allowed on an assigned Account, no automatic rebalance.</summary>
public class InternalTransferPortfolioTests : TestBase
{
    [Test]
    public async Task TransferToAndFromAnAssignedAccount_IsAllowedAndEnqueuesNoRebalance()
    {
        var (_, _, customer, tokens) = await Accounts.AccountHttp.SignInAdviser(
            "Transfer Firm", "transfer-firm", "ann@transfer-firm", customerEmail: "ned@transfer-firm");
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken);
        var brokerage = await InternalTransferTests.OpenAccountAsync(
            tokens.AccessToken, customer.PublicId, "Model", type: "Brokerage");
        var bank = await InternalTransferTests.OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Bank");
        (await TransactionHttp.Send(HttpMethod.Post, "/transactions", tokens.AccessToken, new
            {
                accountId = bank,
                type = "TransferIn",
                amount = 1000m,
                bookedAt = "2026-11-10T08:00:00Z"
            }, Guid.NewGuid()))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        await AssignPortfolioAsync(tokens.AccessToken, brokerage, instrumentId);
        var scheduler = FunctionalTestSetup.WebApiServices.GetRequiredService<RecordingRebalanceScheduler>();
        var before = scheduler.Enqueued.Count;

        await LegAccountsFixture.ReadIdAsync(await InternalTransferTests.PostAsync(
            tokens.AccessToken, InternalTransferTests.Body(bank, brokerage, 600m)));
        var outId = await LegAccountsFixture.ReadIdAsync(await InternalTransferTests.PostAsync(
            tokens.AccessToken, InternalTransferTests.Body(brokerage, bank, 100m)));
        await LegAccountsFixture.ReadIdAsync(await LegAccountsFixture.ReverseAsync(tokens.AccessToken, outId));

        (await InternalTransferTests.CashBalanceAsync(tokens.AccessToken, brokerage)).ShouldBe(600m);
        (await InternalTransferTests.CashBalanceAsync(tokens.AccessToken, bank)).ShouldBe(400m);
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
