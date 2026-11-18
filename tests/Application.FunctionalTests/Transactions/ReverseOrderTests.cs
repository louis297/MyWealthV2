using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Instruments;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Transactions;

/// <summary>fixes/ledger-write-guards.md B1: reverse only the latest live security activity on each holding.</summary>
[Parallelizable]
public class ReverseOrderTests : TestBase
{
    private const string BookedAt = "2026-10-11T10:00:00Z";
    private const string LaterActivityMessage =
        "A later transaction changed this holding. Reverse the later transactions first.";

    [Test]
    public async Task ReverseAnOlderStockIn_Returns400AndLeavesTheHolding()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Brokerage", "USD");
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken, "VTI", "USD");
        var first = await ReadId(await PostSecurityAsync(tokens.AccessToken, accountId, "StockIn", instrumentId, 100m, 1000m));
        await ReadId(await PostSecurityAsync(tokens.AccessToken, accountId, "StockIn", instrumentId, 100m, 2000m));
        await ReadId(await PostSecurityAsync(tokens.AccessToken, accountId, "StockOut", instrumentId, 100m));
        var before = await CountTransactionsAsync(accountId);

        var response = await ReverseAsync(tokens.AccessToken, first);

        await AssertLaterActivityAsync(response);
        var holding = await HoldingAsync(tokens.AccessToken, accountId, instrumentId);
        holding.Quantity.ShouldBe(100m);
        holding.Cost.ShouldBe(1500m);
        (await CountTransactionsAsync(accountId)).ShouldBe(before);
    }

    [Test]
    public async Task ReverseNewestFirst_ClearsTheHoldingAndALaterStockInStartsFromZero()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Brokerage", "USD");
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken, "VTI", "USD");
        var first = await ReadId(await PostSecurityAsync(tokens.AccessToken, accountId, "StockIn", instrumentId, 100m, 1000m));
        var second = await ReadId(await PostSecurityAsync(tokens.AccessToken, accountId, "StockIn", instrumentId, 100m, 2000m));
        var stockOut = await ReadId(await PostSecurityAsync(tokens.AccessToken, accountId, "StockOut", instrumentId, 100m));

        (await ReverseAsync(tokens.AccessToken, stockOut)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await ReverseAsync(tokens.AccessToken, second)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await ReverseAsync(tokens.AccessToken, first)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await HoldingCountAsync(tokens.AccessToken, accountId)).ShouldBe(0);

        await ReadId(await PostSecurityAsync(tokens.AccessToken, accountId, "StockIn", instrumentId, 10m, 100m));
        var holding = await HoldingAsync(tokens.AccessToken, accountId, instrumentId);
        holding.Quantity.ShouldBe(10m);
        holding.Cost.ShouldBe(100m);
    }

    [Test]
    public async Task AReversedPairIsNotLive()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Brokerage", "USD");
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken, "VTI", "USD");
        var a = await ReadId(await PostSecurityAsync(tokens.AccessToken, accountId, "StockIn", instrumentId, 100m, 1000m));
        var b = await ReadId(await PostSecurityAsync(tokens.AccessToken, accountId, "StockIn", instrumentId, 50m, 700m));

        (await ReverseAsync(tokens.AccessToken, b)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await ReverseAsync(tokens.AccessToken, a)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await HoldingCountAsync(tokens.AccessToken, accountId)).ShouldBe(0);
    }

    [Test]
    public async Task ReverseAnOlderBuy_Returns400EvenWhenQuantityAndCashStayNonNegative()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Brokerage", "USD");
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken, "VTI", "USD");
        await SetPriceAsync(instrumentId, 10m, "USD");
        (await PostCashAsync(tokens.AccessToken, accountId, 10000m)).StatusCode.ShouldBe(HttpStatusCode.Created);
        var firstBuy = await ReadId(await PostSecurityAsync(tokens.AccessToken, accountId, "Buy", instrumentId, 100m));
        await ReadId(await PostSecurityAsync(tokens.AccessToken, accountId, "Buy", instrumentId, 100m));
        await ReadId(await PostSecurityAsync(tokens.AccessToken, accountId, "Sell", instrumentId, 100m));
        var before = await CountTransactionsAsync(accountId);

        var response = await ReverseAsync(tokens.AccessToken, firstBuy);

        await AssertLaterActivityAsync(response);
        (await CashBalanceAsync(tokens.AccessToken, accountId)).ShouldBe(9000m);
        var holding = await HoldingAsync(tokens.AccessToken, accountId, instrumentId);
        holding.Quantity.ShouldBe(100m);
        holding.Cost.ShouldBe(1000m);
        (await CountTransactionsAsync(accountId)).ShouldBe(before);
    }

    [Test]
    public async Task ReverseOpeningHoldings_WithLaterActivityOnOneInstrument_Returns400ForBoth()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Brokerage", "USD");
        var vti = await CreateInstrumentAsync(tokens.AccessToken, "VTI", "USD");
        var voo = await CreateInstrumentAsync(tokens.AccessToken, "VOO", "USD");
        var opening = await ReadId(await TransactionHttp.Send(HttpMethod.Post, "/transactions", tokens.AccessToken, new
        {
            accountId,
            type = "OpeningHoldings",
            bookedAt = BookedAt,
            security = new[]
            {
                new { instrumentId = vti, quantity = 100m, cost = 1000m },
                new { instrumentId = voo, quantity = 20m, cost = 400m }
            }
        }, Guid.NewGuid()));
        await ReadId(await PostSecurityAsync(tokens.AccessToken, accountId, "StockIn", vti, 10m, 150m));
        var before = await CountTransactionsAsync(accountId);

        var response = await ReverseAsync(tokens.AccessToken, opening);

        await AssertLaterActivityAsync(response);
        (await HoldingAsync(tokens.AccessToken, accountId, vti)).ShouldBe((110m, 1150m));
        (await HoldingAsync(tokens.AccessToken, accountId, voo)).ShouldBe((20m, 400m));
        (await CountTransactionsAsync(accountId)).ShouldBe(before);
    }

    [Test]
    public async Task ReverseASplit_WithALaterStockIn_Returns400()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Brokerage", "USD");
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken, "VTI", "USD");
        await ReadId(await PostSecurityAsync(tokens.AccessToken, accountId, "StockIn", instrumentId, 100m, 2500m));
        var split = await ReadId(await PostSecurityAsync(tokens.AccessToken, accountId, "Split", instrumentId, 200m));
        await ReadId(await PostSecurityAsync(tokens.AccessToken, accountId, "StockIn", instrumentId, 10m, 100m));

        var response = await ReverseAsync(tokens.AccessToken, split);

        await AssertLaterActivityAsync(response);
        (await HoldingAsync(tokens.AccessToken, accountId, instrumentId)).ShouldBe((210m, 2600m));
    }

    [Test]
    public async Task TheSameInstrumentOnAnotherAccount_DoesNotCount()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var first = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Brokerage", "USD");
        var second = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Other", "USD");
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken, "VTI", "USD");
        var stockIn = await ReadId(await PostSecurityAsync(tokens.AccessToken, first, "StockIn", instrumentId, 100m, 1000m));
        await ReadId(await PostSecurityAsync(tokens.AccessToken, second, "StockIn", instrumentId, 50m, 600m));

        (await ReverseAsync(tokens.AccessToken, stockIn)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await HoldingCountAsync(tokens.AccessToken, first)).ShouldBe(0);
        (await HoldingAsync(tokens.AccessToken, second, instrumentId)).ShouldBe((50m, 600m));
    }

    [Test]
    public async Task CashOnly_AnOlderTransferInCanStillBeReversed()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Bank", "USD");
        var first = await ReadId(await PostCashAsync(tokens.AccessToken, accountId, 100m));
        await ReadId(await PostCashAsync(tokens.AccessToken, accountId, 50m));

        (await ReverseAsync(tokens.AccessToken, first)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await CashBalanceAsync(tokens.AccessToken, accountId)).ShouldBe(50m);
    }

    private static async Task AssertLaterActivityAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, content);
        using var json = JsonDocument.Parse(content);
        json.RootElement.GetProperty("errors").EnumerateObject()
            .SelectMany(property => property.Value.EnumerateArray().Select(value => value.GetString()))
            .ShouldContain(LaterActivityMessage);
    }

    private static Task<HttpResponseMessage> ReverseAsync(string accessToken, Guid id) =>
        TransactionHttp.Send(HttpMethod.Post, $"/transactions/{id}/reverse", accessToken, idempotencyKey: Guid.NewGuid());

    private static Task<HttpResponseMessage> PostCashAsync(string accessToken, Guid accountId, decimal amount) =>
        TransactionHttp.Send(HttpMethod.Post, "/transactions", accessToken, new
        {
            accountId,
            type = "TransferIn",
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

    private static async Task<Guid> ReadId(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.GetProperty("id").GetGuid();
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

    private static async Task<int> HoldingCountAsync(string accessToken, Guid accountId)
    {
        var response = await AccountHttp.Send(HttpMethod.Get, $"/accounts/{accountId}/holdings", accessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("items").GetArrayLength();
    }

    private static async Task<int> CountTransactionsAsync(Guid accountId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var account = await db.Accounts.SingleAsync(row => row.PublicId == accountId);
        return await db.Transactions.CountAsync(row =>
            row.CashLegs.Any(leg => leg.AccountId == account.Id)
            || row.SecurityLegs.Any(leg => leg.AccountId == account.Id));
    }

    private static async Task SetPriceAsync(Guid instrumentPublicId, decimal amount, string currency)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await TestPrices.UpsertAsync(db, instrumentPublicId, amount, currency);
    }

    private static async Task<Guid> OpenAccountAsync(string accessToken, Guid customerId, string type, string currency)
    {
        return await AccountHttp.OpenAccountAsync(accessToken, customerId, name: type + " " + currency, type: type, currency: currency);
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
