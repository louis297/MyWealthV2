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

    [Test]
    public async Task ReverseBuy_RestoresCashAndDeletesTheHolding()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Brokerage", "USD");
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken, "VTI", "USD");
        await SetPriceAsync(instrumentId, 10m, "USD");
        (await PostCashAsync(tokens.AccessToken, accountId, "TransferIn", 100m)).StatusCode
            .ShouldBe(HttpStatusCode.Created);
        var buyId = await ReadId(await PostSecurityAsync(tokens.AccessToken, accountId, "Buy", instrumentId, 2m));

        var reversal = await TransactionHttp.Send(
            HttpMethod.Post, $"/transactions/{buyId}/reverse", tokens.AccessToken, idempotencyKey: Guid.NewGuid());
        reversal.StatusCode.ShouldBe(HttpStatusCode.Created);

        (await CashBalanceAsync(tokens.AccessToken, accountId)).ShouldBe(100m);
        (await AccountHttp.Send(HttpMethod.Get, $"/accounts/{accountId}/holdings", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        using var holdings = JsonDocument.Parse(await (await AccountHttp.Send(
            HttpMethod.Get, $"/accounts/{accountId}/holdings", tokens.AccessToken)).Content.ReadAsStringAsync());
        holdings.RootElement.GetProperty("items").GetArrayLength().ShouldBe(0);

        var original = await TransactionHttp.Send(HttpMethod.Get, $"/transactions/{buyId}", tokens.AccessToken);
        original.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task ReverseBuy_Returns400WhenQuantityWouldGoNegative()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Brokerage", "USD");
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken, "VTI", "USD");
        await SetPriceAsync(instrumentId, 10m, "USD");
        (await PostCashAsync(tokens.AccessToken, accountId, "TransferIn", 100m)).StatusCode
            .ShouldBe(HttpStatusCode.Created);
        var buyId = await ReadId(await PostSecurityAsync(tokens.AccessToken, accountId, "Buy", instrumentId, 2m));
        (await PostSecurityAsync(tokens.AccessToken, accountId, "Sell", instrumentId, 1m)).StatusCode
            .ShouldBe(HttpStatusCode.Created);

        var reversal = await TransactionHttp.Send(
            HttpMethod.Post, $"/transactions/{buyId}/reverse", tokens.AccessToken, idempotencyKey: Guid.NewGuid());
        reversal.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await CashBalanceAsync(tokens.AccessToken, accountId)).ShouldBe(90m);
        var holding = await HoldingAsync(tokens.AccessToken, accountId, instrumentId);
        holding.Quantity.ShouldBe(1m);
        holding.Cost.ShouldBe(10m);
    }

    [Test]
    public async Task ReverseSell_Returns400WhenCashWouldGoNegative()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Brokerage", "USD");
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken, "VTI", "USD");
        await SetPriceAsync(instrumentId, 10m, "USD");
        (await PostCashAsync(tokens.AccessToken, accountId, "TransferIn", 100m)).StatusCode
            .ShouldBe(HttpStatusCode.Created);
        (await PostSecurityAsync(tokens.AccessToken, accountId, "Buy", instrumentId, 2m)).StatusCode
            .ShouldBe(HttpStatusCode.Created);
        var sellId = await ReadId(await PostSecurityAsync(tokens.AccessToken, accountId, "Sell", instrumentId, 2m));
        (await PostCashAsync(tokens.AccessToken, accountId, "TransferOut", -100m)).StatusCode
            .ShouldBe(HttpStatusCode.Created);

        var reversal = await TransactionHttp.Send(
            HttpMethod.Post, $"/transactions/{sellId}/reverse", tokens.AccessToken, idempotencyKey: Guid.NewGuid());
        reversal.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CashBalanceAsync(tokens.AccessToken, accountId)).ShouldBe(0m);
    }

    [Test]
    public async Task MissingPrice_Returns400()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Brokerage", "USD");
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken, "VTI", "USD");
        (await PostCashAsync(tokens.AccessToken, accountId, "TransferIn", 100m)).StatusCode
            .ShouldBe(HttpStatusCode.Created);

        var buy = await PostSecurityAsync(tokens.AccessToken, accountId, "Buy", instrumentId, 1m);
        buy.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CashBalanceAsync(tokens.AccessToken, accountId)).ShouldBe(100m);
        (await HoldingCountAsync(tokens.AccessToken, accountId)).ShouldBe(0);
    }

    [Test]
    public async Task MissingFxPair_Returns400_AndAQuotedPairSetsTheCashAmount()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var jpy = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Brokerage", "JPY");
        var eur = await CreateInstrumentAsync(tokens.AccessToken, "EZU", "EUR");
        await SetPriceAsync(eur, 12m, "EUR");
        (await PostCashAsync(tokens.AccessToken, jpy, "TransferIn", 1000m)).StatusCode
            .ShouldBe(HttpStatusCode.Created);
        (await PostSecurityAsync(tokens.AccessToken, jpy, "Buy", eur, 1m)).StatusCode
            .ShouldBe(HttpStatusCode.BadRequest);
        (await CashBalanceAsync(tokens.AccessToken, jpy)).ShouldBe(1000m);

        var nzd = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Other", "NZD");
        var vti = await CreateInstrumentAsync(tokens.AccessToken, "VTI", "USD");
        await SetPriceAsync(vti, 10m, "USD");
        SetRate("USD", "NZD", 1.6m);
        (await PostCashAsync(tokens.AccessToken, nzd, "TransferIn", 100m)).StatusCode
            .ShouldBe(HttpStatusCode.Created);
        (await PostSecurityAsync(tokens.AccessToken, nzd, "Buy", vti, 2m)).StatusCode
            .ShouldBe(HttpStatusCode.Created);
        (await CashBalanceAsync(tokens.AccessToken, nzd)).ShouldBe(68.0000m);
        var holding = await HoldingAsync(tokens.AccessToken, nzd, vti);
        holding.Quantity.ShouldBe(2m);
        holding.Cost.ShouldBe(20.0000m);
    }

    [Test]
    public async Task SellAndStockOut_AboveTheHolding_Return400()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Brokerage", "USD");
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken, "VTI", "USD");
        await SetPriceAsync(instrumentId, 10m, "USD");
        (await PostSecurityAsync(tokens.AccessToken, accountId, "Sell", instrumentId, 1m)).StatusCode
            .ShouldBe(HttpStatusCode.BadRequest);
        (await PostSecurityAsync(tokens.AccessToken, accountId, "StockOut", instrumentId, 1m)).StatusCode
            .ShouldBe(HttpStatusCode.BadRequest);

        (await PostSecurityAsync(tokens.AccessToken, accountId, "StockIn", instrumentId, 1m, 10m)).StatusCode
            .ShouldBe(HttpStatusCode.Created);
        (await PostSecurityAsync(tokens.AccessToken, accountId, "Sell", instrumentId, 2m)).StatusCode
            .ShouldBe(HttpStatusCode.BadRequest);
        (await PostSecurityAsync(tokens.AccessToken, accountId, "StockOut", instrumentId, 2m)).StatusCode
            .ShouldBe(HttpStatusCode.BadRequest);
        var holding = await HoldingAsync(tokens.AccessToken, accountId, instrumentId);
        holding.Quantity.ShouldBe(1m);
        holding.Cost.ShouldBe(10m);
    }

    [Test]
    public async Task Buy_ThatWouldMakeCashNegative_Returns400()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Brokerage", "USD");
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken, "VTI", "USD");
        await SetPriceAsync(instrumentId, 10m, "USD");
        (await PostCashAsync(tokens.AccessToken, accountId, "TransferIn", 10m)).StatusCode
            .ShouldBe(HttpStatusCode.Created);

        (await PostSecurityAsync(tokens.AccessToken, accountId, "Buy", instrumentId, 2m)).StatusCode
            .ShouldBe(HttpStatusCode.BadRequest);
        (await CashBalanceAsync(tokens.AccessToken, accountId)).ShouldBe(10m);
        (await HoldingCountAsync(tokens.AccessToken, accountId)).ShouldBe(0);
    }

    [Test]
    public async Task DisabledInstrument_Returns400()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInTenantAdmin();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Brokerage", "USD");
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken, "VTI", "USD");
        await SetPriceAsync(instrumentId, 10m, "USD");
        await DisableInstrumentAsync(instrumentId, tokens.AccessToken);
        (await PostCashAsync(tokens.AccessToken, accountId, "TransferIn", 100m)).StatusCode
            .ShouldBe(HttpStatusCode.Created);

        (await PostSecurityAsync(tokens.AccessToken, accountId, "Buy", instrumentId, 1m)).StatusCode
            .ShouldBe(HttpStatusCode.BadRequest);
    }

    [TestCase("Bank")]
    [TestCase("Cash")]
    public async Task BankAndCash_Return400(string accountType)
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, accountType, "USD");
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken, "VTI", "USD");
        await SetPriceAsync(instrumentId, 10m, "USD");
        (await PostCashAsync(tokens.AccessToken, accountId, "TransferIn", 100m)).StatusCode
            .ShouldBe(HttpStatusCode.Created);

        (await PostSecurityAsync(tokens.AccessToken, accountId, "Buy", instrumentId, 1m)).StatusCode
            .ShouldBe(HttpStatusCode.BadRequest);
        (await PostSecurityAsync(tokens.AccessToken, accountId, "StockIn", instrumentId, 1m, 10m)).StatusCode
            .ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task ForbiddenPriceCostAndCashFields_Return400()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Brokerage", "USD");
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken, "VTI", "USD");
        await SetPriceAsync(instrumentId, 10m, "USD");
        (await PostCashAsync(tokens.AccessToken, accountId, "TransferIn", 100m)).StatusCode
            .ShouldBe(HttpStatusCode.Created);

        (await PostBodyAsync(tokens.AccessToken, new
        {
            accountId,
            type = "Buy",
            bookedAt = BookedAt,
            security = new[] { new { instrumentId, quantity = 1m, cost = 10m } }
        })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await PostBodyAsync(tokens.AccessToken, new
        {
            accountId,
            type = "Buy",
            bookedAt = BookedAt,
            security = new[] { new { instrumentId, quantity = 1m, price = 10m } }
        })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await PostBodyAsync(tokens.AccessToken, new
        {
            accountId,
            type = "StockIn",
            bookedAt = BookedAt,
            amount = 10m,
            security = new[] { new { instrumentId, quantity = 1m, cost = 10m } }
        })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await PostBodyAsync(tokens.AccessToken, new
        {
            accountId,
            type = "StockIn",
            bookedAt = BookedAt,
            cash = new { amount = 10m, currency = "USD" },
            security = new[] { new { instrumentId, quantity = 1m, cost = 10m } }
        })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await PostSecurityAsync(tokens.AccessToken, accountId, "StockIn", instrumentId, 1m)).StatusCode
            .ShouldBe(HttpStatusCode.BadRequest);
        (await CashBalanceAsync(tokens.AccessToken, accountId)).ShouldBe(100m);
        (await HoldingCountAsync(tokens.AccessToken, accountId)).ShouldBe(0);
    }

    [Test]
    public async Task IdempotencyKey_ReplaysTheBuyAndRejectsADifferentQuantity()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Brokerage", "USD");
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken, "VTI", "USD");
        await SetPriceAsync(instrumentId, 10m, "USD");
        (await PostCashAsync(tokens.AccessToken, accountId, "TransferIn", 100m)).StatusCode
            .ShouldBe(HttpStatusCode.Created);
        var key = Guid.NewGuid();

        var id = await ReadId(await PostSecurityAsync(tokens.AccessToken, accountId, "Buy", instrumentId, 1m, key: key));
        var replay = await PostSecurityAsync(tokens.AccessToken, accountId, "Buy", instrumentId, 1m, key: key);
        replay.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await ReadId(replay)).ShouldBe(id);

        var mismatch = await PostSecurityAsync(tokens.AccessToken, accountId, "Buy", instrumentId, 2m, key: key);
        mismatch.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CashBalanceAsync(tokens.AccessToken, accountId)).ShouldBe(90m);
    }

    [Test]
    public async Task OtherTenantAccountOrInstrument_Returns404()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Brokerage", "USD");
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken, "VTI", "USD");
        await SetPriceAsync(instrumentId, 10m, "USD");
        (await PostCashAsync(tokens.AccessToken, accountId, "TransferIn", 100m)).StatusCode
            .ShouldBe(HttpStatusCode.Created);

        var (_, _, southCustomer, southTokens) = await AccountHttp.SignInTenantAdmin(
            "South Advisory", "south-advisory", "tess@south.example", adviserEmail: "ann@south.example",
            customerEmail: "ned@south.example");
        var southAccount = await OpenAccountAsync(southTokens.AccessToken, southCustomer.PublicId, "Brokerage", "USD");
        var southInstrument = await CreateInstrumentAsync(southTokens.AccessToken, "AIA", "USD");
        await SetPriceAsync(southInstrument, 8m, "USD");

        (await PostSecurityAsync(tokens.AccessToken, southAccount, "Buy", instrumentId, 1m)).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
        (await PostSecurityAsync(tokens.AccessToken, accountId, "Buy", southInstrument, 1m)).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
        (await CashBalanceAsync(tokens.AccessToken, accountId)).ShouldBe(100m);
    }

    [Test]
    public async Task ItemShape_UsesCashAndSecurityAndListsAStockIn()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Brokerage", "NZD");
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken, "VTI", "USD");
        await SetPriceAsync(instrumentId, 15m, "USD");
        SetRate("USD", "NZD", 5m / 3m);
        (await PostCashAsync(tokens.AccessToken, accountId, "TransferIn", 2000m)).StatusCode
            .ShouldBe(HttpStatusCode.Created);
        var buyId = await ReadId(await PostSecurityAsync(tokens.AccessToken, accountId, "Buy", instrumentId, 50m));
        var stockInId = await ReadId(await PostSecurityAsync(
            tokens.AccessToken, accountId, "StockIn", instrumentId, 4m, 800m));

        using var buy = await GetItemAsync(tokens.AccessToken, buyId);
        buy.RootElement.TryGetProperty("amount", out _).ShouldBeFalse();
        buy.RootElement.TryGetProperty("currency", out _).ShouldBeFalse();
        buy.RootElement.GetProperty("cash").GetProperty("amount").GetDecimal().ShouldBe(-1250.0000m);
        buy.RootElement.GetProperty("cash").GetProperty("currency").GetString().ShouldBe("NZD");
        var buyLeg = buy.RootElement.GetProperty("security").EnumerateArray().Single();
        buyLeg.GetProperty("quantity").GetDecimal().ShouldBe(50m);
        buyLeg.GetProperty("cost").GetDecimal().ShouldBe(750.0000m);
        buyLeg.GetProperty("costCurrency").GetString().ShouldBe("USD");

        using var stockIn = await GetItemAsync(tokens.AccessToken, stockInId);
        stockIn.RootElement.TryGetProperty("cash", out _).ShouldBeFalse();
        stockIn.RootElement.GetProperty("security").EnumerateArray().Single()
            .GetProperty("costCurrency").GetString().ShouldBe("USD");

        var list = await TransactionHttp.Send(
            HttpMethod.Get, $"/transactions?accountId={accountId}", tokens.AccessToken);
        list.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        var ids = json.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .ToList();
        ids.ShouldContain(stockInId);
        var listedBuy = json.RootElement.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("id").GetGuid() == buyId);
        listedBuy.GetProperty("cash").GetProperty("amount").GetDecimal().ShouldBe(-1250.0000m);
        listedBuy.TryGetProperty("security", out var listedSecurity).ShouldBeTrue();
        listedSecurity.EnumerateArray().Single().GetProperty("costCurrency").GetString().ShouldBe("USD");
    }

    private static async Task<JsonDocument> GetItemAsync(string accessToken, Guid id)
    {
        var response = await TransactionHttp.Send(HttpMethod.Get, $"/transactions/{id}", accessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static async Task<Guid> ReadId(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
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
        decimal? cost = null,
        Guid? key = null) =>
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
        }, key ?? Guid.NewGuid());

    private static Task<HttpResponseMessage> PostBodyAsync(string accessToken, object body) =>
        TransactionHttp.Send(HttpMethod.Post, "/transactions", accessToken, body, Guid.NewGuid());

    private static void SetRate(string from, string to, decimal rate) =>
        FunctionalTestSetup.WebApiServices.GetRequiredService<FxRateMock>().SetRate(from, to, rate);

    private static async Task<int> HoldingCountAsync(string accessToken, Guid accountId)
    {
        var response = await AccountHttp.Send(HttpMethod.Get, $"/accounts/{accountId}/holdings", accessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("items").GetArrayLength();
    }

    private static async Task DisableInstrumentAsync(Guid instrumentId, string accessToken)
    {
        var get = await InstrumentHttp.Send(HttpMethod.Get, $"/instruments/{instrumentId}", accessToken);
        get.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        var rowVersion = json.RootElement.GetProperty("rowVersion").GetString();
        var disable = await InstrumentHttp.Send(
            HttpMethod.Post, $"/instruments/{instrumentId}/disable", accessToken, new { rowVersion });
        disable.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

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
