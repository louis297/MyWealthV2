using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Instruments;
using MyWealthV2.Domain.ValueObjects;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.MarketData;

namespace MyWealthV2.Application.FunctionalTests.Transactions;

public class BuySellTests : TestBase
{
    private const string BookedAt = "2026-10-11T10:00:00+13:00";

    [Test]
    public async Task BuyThenSell_UsesTheMockPriceAndAverageCostWithoutAnFxRow()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Brokerage", "USD");
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken, "VTI", "USD");
        await SetPriceAsync(instrumentId, 10m / 3m, "USD");
        (await PostCashAsync(tokens.AccessToken, accountId, "TransferIn", 20m)).StatusCode
            .ShouldBe(HttpStatusCode.Created);

        var buy = await PostSecurityAsync(tokens.AccessToken, accountId, "Buy", instrumentId, 3m);
        buy.StatusCode.ShouldBe(HttpStatusCode.Created);

        (await CashBalanceAsync(tokens.AccessToken, accountId)).ShouldBe(10.0000m);
        var afterBuy = await HoldingAsync(tokens.AccessToken, accountId, instrumentId);
        afterBuy.Quantity.ShouldBe(3m);
        afterBuy.Cost.ShouldBe(10.0000m);

        var sell = await PostSecurityAsync(tokens.AccessToken, accountId, "Sell", instrumentId, 1m);
        sell.StatusCode.ShouldBe(HttpStatusCode.Created);

        (await CashBalanceAsync(tokens.AccessToken, accountId)).ShouldBe(13.3333m);
        var afterSell = await HoldingAsync(tokens.AccessToken, accountId, instrumentId);
        afterSell.Quantity.ShouldBe(2m);
        afterSell.Cost.ShouldBe(6.6667m);
    }

    [Test]
    public async Task StockInThenStockOut_MovesQuantityAndCostWithoutCash()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Other", "NZD");
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken, "AIA", "USD");

        var stockIn = await PostSecurityAsync(tokens.AccessToken, accountId, "StockIn", instrumentId, 3m, 10m);
        stockIn.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await CashBalanceAsync(tokens.AccessToken, accountId)).ShouldBe(0m);
        var afterIn = await HoldingAsync(tokens.AccessToken, accountId, instrumentId);
        afterIn.Quantity.ShouldBe(3m);
        afterIn.Cost.ShouldBe(10.0000m);

        var stockOut = await PostSecurityAsync(tokens.AccessToken, accountId, "StockOut", instrumentId, 1m);
        stockOut.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await CashBalanceAsync(tokens.AccessToken, accountId)).ShouldBe(0m);
        var afterOut = await HoldingAsync(tokens.AccessToken, accountId, instrumentId);
        afterOut.Quantity.ShouldBe(2m);
        afterOut.Cost.ShouldBe(6.6667m);
    }

    private static async Task SetPriceAsync(Guid instrumentPublicId, decimal amount, string currency)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var instrument = await db.Instruments.SingleAsync(row => row.PublicId == instrumentPublicId);
        var prices = FunctionalTestSetup.WebApiServices.GetRequiredService<MarketDataMock>();
        prices.SetPrice(instrument.Id, Money.Create(amount, currency));
    }

    private static async Task<decimal> CashBalanceAsync(string accessToken, Guid accountId)
    {
        var response = await AccountHttp.Send(HttpMethod.Get, $"/accounts/{accountId}/cash-balance", accessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("cashBalance").GetDecimal();
    }

    private static async Task<(decimal Quantity, decimal Cost)> HoldingAsync(
        string accessToken,
        Guid accountId,
        Guid instrumentId)
    {
        var response = await AccountHttp.Send(HttpMethod.Get, $"/accounts/{accountId}/holdings", accessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = json.RootElement.GetProperty("items").EnumerateArray()
            .Single(row => row.GetProperty("instrumentId").GetGuid() == instrumentId);
        return (item.GetProperty("quantity").GetDecimal(), item.GetProperty("cost").GetDecimal());
    }

    private static Task<HttpResponseMessage> PostCashAsync(
        string accessToken,
        Guid accountId,
        string type,
        decimal amount) =>
        TransactionHttp.Send(HttpMethod.Post, "/transactions", accessToken, new
        {
            accountId,
            type,
            amount,
            bookedAt = BookedAt
        }, Guid.NewGuid());

    private static Task<HttpResponseMessage> PostSecurityAsync(
        string accessToken,
        Guid accountId,
        string type,
        Guid instrumentId,
        decimal quantity,
        decimal? cost = null) =>
        TransactionHttp.Send(HttpMethod.Post, "/transactions", accessToken, new
        {
            accountId,
            type,
            bookedAt = BookedAt,
            security = new[]
            {
                cost is null
                    ? (object)new { instrumentId, quantity }
                    : new { instrumentId, quantity, cost }
            }
        }, Guid.NewGuid());

    private static async Task<Guid> OpenAccountAsync(
        string accessToken,
        Guid customerId,
        string type,
        string currency)
    {
        var response = await AccountHttp.Send(HttpMethod.Post, "/accounts", accessToken, new
        {
            customerId,
            name = type + " " + currency,
            type,
            currency
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateInstrumentAsync(string accessToken, string symbol, string quoteCurrency)
    {
        var response = await InstrumentHttp.Send(HttpMethod.Post, "/instruments", accessToken, new
        {
            symbol,
            name = symbol + " fund",
            quoteCurrency
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }
}
