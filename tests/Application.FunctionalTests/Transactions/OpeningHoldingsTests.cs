using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Instruments;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Transactions;

public class OpeningHoldingsTests : TestBase
{
    private const string BookedAt = "2026-09-27T09:00:00+12:00";

    [Test]
    public async Task Adviser_OpeningHoldingsOnBrokerage_Returns201AndGetHasSecurityWithoutCashAmount()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Brokerage", "Brokerage book");
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken, "VTI", "USD");

        var response = await PostOpeningAsync(
            tokens.AccessToken, accountId, new[] { Leg(instrumentId, 100m, 1500.00m) });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        created.RootElement.EnumerateObject().Select(property => property.Name).ShouldBe(["id"]);
        var id = created.RootElement.GetProperty("id").GetGuid();

        using var item = await GetItemAsync(tokens.AccessToken, id);
        item.RootElement.GetProperty("type").GetString().ShouldBe("OpeningHoldings");
        AssertNoCashAmount(item.RootElement);
        var leg = item.RootElement.GetProperty("security").EnumerateArray().Single();
        leg.GetProperty("instrumentId").GetGuid().ShouldBe(instrumentId);
        leg.GetProperty("quantity").GetDecimal().ShouldBe(100m);
        leg.GetProperty("cost").GetDecimal().ShouldBe(1500.00m);
        leg.GetProperty("quoteCurrency").GetString().ShouldBe("USD");
    }

    [Test]
    public async Task OtherAccount_AndASecondInstrument_AreAllowed()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Other", "Other book");
        var first = await CreateInstrumentAsync(tokens.AccessToken, "VTI", "USD");
        var second = await CreateInstrumentAsync(tokens.AccessToken, "VXUS", "USD");

        (await PostOpeningAsync(tokens.AccessToken, accountId, new[] { Leg(first) }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        (await PostOpeningAsync(tokens.AccessToken, accountId, new[] { Leg(second, 2m, 20m) }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Test]
    public async Task OneHeader_MayOpenTwoInstruments()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Brokerage", "Two names");
        var first = await CreateInstrumentAsync(tokens.AccessToken, "VTI", "USD");
        var second = await CreateInstrumentAsync(tokens.AccessToken, "BND", "USD");

        var id = await ReadId(await PostOpeningAsync(
            tokens.AccessToken,
            accountId,
            new[] { Leg(first, 100m, 1500.00m), Leg(second, 2m, 0m) }));

        using var item = await GetItemAsync(tokens.AccessToken, id);
        item.RootElement.GetProperty("type").GetString().ShouldBe("OpeningHoldings");
        var legs = item.RootElement.GetProperty("security").EnumerateArray()
            .ToDictionary(leg => leg.GetProperty("instrumentId").GetGuid());
        legs.Count.ShouldBe(2);
        legs[first].GetProperty("quantity").GetDecimal().ShouldBe(100m);
        legs[first].GetProperty("cost").GetDecimal().ShouldBe(1500.00m);
        legs[first].GetProperty("quoteCurrency").GetString().ShouldBe("USD");
        legs[second].GetProperty("quantity").GetDecimal().ShouldBe(2m);
        legs[second].GetProperty("cost").GetDecimal().ShouldBe(0m);
        legs[second].GetProperty("quoteCurrency").GetString().ShouldBe("USD");
    }

    [Test]
    public async Task SecondHeaderForTheSameInstrument_Returns400()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Brokerage", "Brokerage book");
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken, "VTI", "USD");

        await ReadId(await PostOpeningAsync(tokens.AccessToken, accountId, new[] { Leg(instrumentId) }));

        var second = await PostOpeningAsync(tokens.AccessToken, accountId, new[] { Leg(instrumentId, 1m, 5m) });
        second.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task SameInstrumentTwiceOnOneHeader_Returns400()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Brokerage", "Brokerage book");
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken, "VTI", "USD");

        var response = await PostOpeningAsync(
            tokens.AccessToken,
            accountId,
            new[] { Leg(instrumentId, 1m, 10m), Leg(instrumentId, 2m, 0m) });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [TestCase("Bank")]
    [TestCase("Cash")]
    public async Task BankAndCash_Return400(string accountType)
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, accountType, accountType + " book");
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken, "VTI", "USD");

        var response = await PostOpeningAsync(tokens.AccessToken, accountId, new[] { Leg(instrumentId) });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task DisabledInstrument_Returns400()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInTenantAdmin();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Brokerage", "Brokerage book");
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken, "VTI", "USD");
        await DisableInstrumentAsync(instrumentId, tokens.AccessToken);

        var response = await PostOpeningAsync(tokens.AccessToken, accountId, new[] { Leg(instrumentId) });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task UnknownInstrument_Returns404()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Brokerage", "Brokerage book");

        var response = await PostOpeningAsync(tokens.AccessToken, accountId, new[] { Leg(Guid.NewGuid()) });
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task AmountOrCash_Returns400()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Brokerage", "Brokerage book");
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken, "VTI", "USD");
        var security = new[] { Leg(instrumentId) };

        (await PostOpeningAsync(tokens.AccessToken, accountId, security, amount: 100m))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await PostOpeningAsync(tokens.AccessToken, accountId, security, cash: new { amount = 10.00m, currency = "NZD" }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task EmptySecurityQuantityAndNegativeCost_Return400_AndZeroCostIsAllowed()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Brokerage", "Brokerage book");
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken, "VTI", "USD");

        (await PostOpeningAsync(tokens.AccessToken, accountId, Array.Empty<object>()))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await PostOpeningAsync(tokens.AccessToken, accountId, new[] { Leg(instrumentId, 0m, 10m) }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await PostOpeningAsync(tokens.AccessToken, accountId, new[] { Leg(instrumentId, 1m, -0.01m) }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var id = await ReadId(await PostOpeningAsync(
            tokens.AccessToken, accountId, new[] { Leg(instrumentId, 1m, 0m) }));
        using var item = await GetItemAsync(tokens.AccessToken, id);
        item.RootElement.GetProperty("security").EnumerateArray().Single()
            .GetProperty("cost").GetDecimal().ShouldBe(0m);
    }

    [Test]
    public async Task IdempotencyKey_IsRequiredAndReplaysTheSameId()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Brokerage", "Brokerage book");
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken, "VTI", "USD");
        var security = new[] { Leg(instrumentId) };

        var missing = await PostOpeningAsync(tokens.AccessToken, accountId, security, key: Guid.Empty);
        missing.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var key = Guid.NewGuid();
        var id = await ReadId(await PostOpeningAsync(tokens.AccessToken, accountId, security, key: key));
        var replay = await PostOpeningAsync(tokens.AccessToken, accountId, security, key: key);
        replay.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await ReadId(replay)).ShouldBe(id);
    }

    [Test]
    public async Task CashOpeningAfterOpeningHoldings_Returns400_AndOpeningHoldingsAfterCashOpening_Returns201()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var holdingsFirst = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Brokerage", "Holdings first");
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken, "VTI", "USD");

        (await PostOpeningAsync(tokens.AccessToken, holdingsFirst, new[] { Leg(instrumentId) }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        (await PostCashOpeningAsync(tokens.AccessToken, holdingsFirst, "2026-09-27T10:00:00+12:00"))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var cashFirst = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Brokerage", "Cash first");
        var otherInstrument = await CreateInstrumentAsync(tokens.AccessToken, "VXUS", "USD");
        (await PostCashOpeningAsync(tokens.AccessToken, cashFirst, "2026-09-27T09:00:00+12:00"))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        (await PostOpeningAsync(tokens.AccessToken, cashFirst, new[] { Leg(otherInstrument) }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Test]
    public async Task ClosedAccount_Returns400()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Brokerage", "Closed book");
        await CloseAccountAsync(accountId, tokens.AccessToken);

        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken, "VTI", "USD");
        var response = await PostOpeningAsync(tokens.AccessToken, accountId, new[] { Leg(instrumentId) });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task DisabledCustomer_Returns400TargetUser()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Brokerage", "Brokerage book");
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken, "VTI", "USD");
        await DisableCustomerAsync(customer.Id);

        var response = await PostOpeningAsync(tokens.AccessToken, accountId, new[] { Leg(instrumentId) });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("code").GetString().ShouldBe("disabled");
        json.RootElement.GetProperty("target").GetString().ShouldBe("user");
    }

    private static object Leg(Guid instrumentId, decimal quantity = 100m, decimal cost = 1500.00m) =>
        new { instrumentId, quantity, cost };

    private static async Task<HttpResponseMessage> PostOpeningAsync(
        string accessToken,
        Guid accountId,
        object security,
        Guid? key = null,
        decimal? amount = null,
        object? cash = null,
        string bookedAt = BookedAt)
    {
        var body = new Dictionary<string, object?>
        {
            ["accountId"] = accountId,
            ["type"] = "OpeningHoldings",
            ["bookedAt"] = bookedAt,
            ["security"] = security
        };
        if (amount is not null)
        {
            body["amount"] = amount;
        }

        if (cash is not null)
        {
            body["cash"] = cash;
        }

        Guid? idempotencyKey = key == Guid.Empty ? null : key ?? Guid.NewGuid();
        return await TransactionHttp.Send(HttpMethod.Post, "/transactions", accessToken, body, idempotencyKey);
    }

    private static Task<HttpResponseMessage> PostCashOpeningAsync(
        string accessToken,
        Guid accountId,
        string bookedAt) =>
        TransactionHttp.Send(HttpMethod.Post, "/transactions", accessToken, new
        {
            accountId,
            type = "Opening",
            amount = 25m,
            bookedAt
        }, Guid.NewGuid());

    private static async Task<Guid> ReadId(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.EnumerateObject().Select(property => property.Name).ShouldBe(["id"]);
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<JsonDocument> GetItemAsync(string accessToken, Guid id)
    {
        var response = await TransactionHttp.Send(HttpMethod.Get, $"/transactions/{id}", accessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static void AssertNoCashAmount(JsonElement item)
    {
        if (item.TryGetProperty("amount", out var amount))
        {
            amount.ValueKind.ShouldBe(JsonValueKind.Null);
        }

        if (item.TryGetProperty("cash", out var cash))
        {
            cash.ValueKind.ShouldBe(JsonValueKind.Null);
        }
    }

    private static async Task<Guid> OpenAccountAsync(
        string accessToken,
        Guid customerId,
        string type,
        string name)
    {
        var response = await AccountHttp.Send(HttpMethod.Post, "/accounts", accessToken, new
        {
            customerId,
            name,
            type,
            currency = "NZD"
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

    private static async Task CloseAccountAsync(Guid accountId, string accessToken)
    {
        var get = await AccountHttp.Send(HttpMethod.Get, $"/accounts/{accountId}", accessToken);
        using var json = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        var rowVersion = json.RootElement.GetProperty("rowVersion").GetString();
        var close = await AccountHttp.Send(
            HttpMethod.Post, $"/accounts/{accountId}/close", accessToken, new { rowVersion });
        close.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private static async Task DisableCustomerAsync(int customerId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var person = await db.DomainUsers.SingleAsync(row => row.Id == customerId);
        person.Disable();
        await db.SaveChangesAsync();
    }
}
