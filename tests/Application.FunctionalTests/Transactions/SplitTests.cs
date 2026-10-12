using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Instruments;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.Transactions;

public class SplitTests : TestBase
{
    private const string BookedAt = "2026-10-12T10:00:00+13:00";

    [Test]
    public async Task WithoutBearer_Returns401WithoutRedirect()
    {
        var post = await TransactionHttp.Send(HttpMethod.Post, "/transactions", accessToken: null, new
        {
            accountId = Guid.NewGuid(),
            type = "Split",
            bookedAt = BookedAt,
            security = new[] { new { instrumentId = Guid.NewGuid(), quantity = 2m } }
        });

        post.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        post.Headers.Location.ShouldBeNull();
    }

    [Test]
    public async Task Customer_Returns403()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm Split", "firmsplit");
        var adviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Cara", "cara@firmsplit", "Password1!", tenant.Id);
        var customer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Dee", "dee@firmsplit", "Password1!", tenant.Id, adviser.Id);
        var tokens = await TestApp.IssueAccessTokenAsync(customer);

        var post = await PostSplitAsync(tokens.AccessToken, Guid.NewGuid(), "Split", Guid.NewGuid(), 2m);
        post.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task Split_DoublesQuantityAndLeavesTotalCostUnchanged()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Brokerage", "USD");
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken, "VTI", "USD");
        (await PostOpeningAsync(tokens.AccessToken, accountId, instrumentId, 100m, 2500m)).StatusCode
            .ShouldBe(HttpStatusCode.Created);

        var created = await PostSplitAsync(tokens.AccessToken, accountId, "Split", instrumentId, 200m);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        body.RootElement.EnumerateObject().Select(property => property.Name).ShouldBe(["id"]);
        var id = body.RootElement.GetProperty("id").GetGuid();

        var holding = await HoldingAsync(tokens.AccessToken, accountId, instrumentId);
        holding.Quantity.ShouldBe(200m);
        holding.Cost.ShouldBe(2500m);

        using var item = await GetItemAsync(tokens.AccessToken, id);
        item.RootElement.GetProperty("type").GetString().ShouldBe("Split");
        item.RootElement.TryGetProperty("cash", out _).ShouldBeFalse();
        var leg = item.RootElement.GetProperty("security").EnumerateArray().Single();
        leg.GetProperty("instrumentId").GetGuid().ShouldBe(instrumentId);
        leg.GetProperty("quantity").GetDecimal().ShouldBe(100m);
        leg.GetProperty("cost").GetDecimal().ShouldBe(0m);
        leg.GetProperty("costCurrency").GetString().ShouldBe("USD");
    }

    [TestCase("Bonus", 115)]
    [TestCase("Scrip", 110)]
    public async Task BonusAndScrip_ChangeQuantityAndLeaveTotalCostUnchanged(string type, decimal newQuantity)
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Other", "USD");
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken, type, "USD");
        (await PostOpeningAsync(tokens.AccessToken, accountId, instrumentId, 100m, 800m)).StatusCode
            .ShouldBe(HttpStatusCode.Created);

        (await PostSplitAsync(tokens.AccessToken, accountId, type, instrumentId, newQuantity)).StatusCode
            .ShouldBe(HttpStatusCode.Created);

        var holding = await HoldingAsync(tokens.AccessToken, accountId, instrumentId);
        holding.Quantity.ShouldBe(newQuantity);
        holding.Cost.ShouldBe(800m);
    }

    [Test]
    public async Task ReverseSplit_ReducesQuantityAndLeavesTotalCostUnchanged()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Brokerage", "USD");
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken, "VTI", "USD");
        (await PostOpeningAsync(tokens.AccessToken, accountId, instrumentId, 100m, 2500m)).StatusCode
            .ShouldBe(HttpStatusCode.Created);

        (await PostSplitAsync(tokens.AccessToken, accountId, "Split", instrumentId, 50m)).StatusCode
            .ShouldBe(HttpStatusCode.Created);

        var holding = await HoldingAsync(tokens.AccessToken, accountId, instrumentId);
        holding.Quantity.ShouldBe(50m);
        holding.Cost.ShouldBe(2500m);
    }

    [Test]
    public async Task Reverse_RestoresQuantityAndCost()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Brokerage", "USD");
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken, "VTI", "USD");
        (await PostOpeningAsync(tokens.AccessToken, accountId, instrumentId, 100m, 2500m)).StatusCode
            .ShouldBe(HttpStatusCode.Created);
        var splitId = await ReadId(await PostSplitAsync(tokens.AccessToken, accountId, "Split", instrumentId, 200m));
        decimal quantity;
        decimal cost;
        using (var before = await GetItemAsync(tokens.AccessToken, splitId))
        {
            var leg = before.RootElement.GetProperty("security").EnumerateArray().Single();
            quantity = leg.GetProperty("quantity").GetDecimal();
            cost = leg.GetProperty("cost").GetDecimal();
        }

        var reversalId = await ReadId(await TransactionHttp.Send(
            HttpMethod.Post, $"/transactions/{splitId}/reverse", tokens.AccessToken, idempotencyKey: Guid.NewGuid()));

        var holding = await HoldingAsync(tokens.AccessToken, accountId, instrumentId);
        holding.Quantity.ShouldBe(100m);
        holding.Cost.ShouldBe(2500m);

        using var original = await GetItemAsync(tokens.AccessToken, splitId);
        var originalLeg = original.RootElement.GetProperty("security").EnumerateArray().Single();
        originalLeg.GetProperty("quantity").GetDecimal().ShouldBe(quantity);
        originalLeg.GetProperty("cost").GetDecimal().ShouldBe(cost);
        original.RootElement.GetProperty("originalTransactionId").ValueKind.ShouldBe(JsonValueKind.Null);
        original.RootElement.GetProperty("reversedByTransactionId").GetGuid().ShouldBe(reversalId);

        using var reversal = await GetItemAsync(tokens.AccessToken, reversalId);
        reversal.RootElement.GetProperty("type").GetString().ShouldBe("Reversal");
        var reversalLeg = reversal.RootElement.GetProperty("security").EnumerateArray().Single();
        reversalLeg.GetProperty("quantity").GetDecimal().ShouldBe(-quantity);
        reversalLeg.GetProperty("cost").GetDecimal().ShouldBe(0m);
        reversal.RootElement.GetProperty("originalTransactionId").GetGuid().ShouldBe(splitId);
        reversal.RootElement.GetProperty("reversedByTransactionId").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task NoHolding_Returns400()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Brokerage", "USD");
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken, "VTI", "USD");

        (await PostSplitAsync(tokens.AccessToken, accountId, "Split", instrumentId, 200m)).StatusCode
            .ShouldBe(HttpStatusCode.BadRequest);
        (await HoldingCountAsync(tokens.AccessToken, accountId)).ShouldBe(0);
    }

    [Test]
    public async Task QuantityEqualToCurrent_Returns400()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Brokerage", "USD");
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken, "VTI", "USD");
        (await PostOpeningAsync(tokens.AccessToken, accountId, instrumentId, 100m, 2500m)).StatusCode
            .ShouldBe(HttpStatusCode.Created);

        (await PostSplitAsync(tokens.AccessToken, accountId, "Bonus", instrumentId, 100m)).StatusCode
            .ShouldBe(HttpStatusCode.BadRequest);

        var holding = await HoldingAsync(tokens.AccessToken, accountId, instrumentId);
        holding.Quantity.ShouldBe(100m);
        holding.Cost.ShouldBe(2500m);
    }

    [Test]
    public async Task CashAmountRatioCostAndPrice_Return400()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Brokerage", "USD");
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken, "VTI", "USD");
        (await PostOpeningAsync(tokens.AccessToken, accountId, instrumentId, 100m, 2500m)).StatusCode
            .ShouldBe(HttpStatusCode.Created);

        (await PostBodyAsync(tokens.AccessToken, new
        {
            accountId,
            type = "Split",
            bookedAt = BookedAt,
            amount = 10m,
            security = new[] { new { instrumentId, quantity = 200m } }
        })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await PostBodyAsync(tokens.AccessToken, new
        {
            accountId,
            type = "Split",
            bookedAt = BookedAt,
            cash = new { amount = 10m, currency = "USD" },
            security = new[] { new { instrumentId, quantity = 200m } }
        })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await PostBodyAsync(tokens.AccessToken, new
        {
            accountId,
            type = "Bonus",
            bookedAt = BookedAt,
            ratio = 2m,
            security = new[] { new { instrumentId, quantity = 200m } }
        })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await PostBodyAsync(tokens.AccessToken, new
        {
            accountId,
            type = "Scrip",
            bookedAt = BookedAt,
            security = new[] { new { instrumentId, quantity = 200m, ratio = 2m } }
        })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await PostBodyAsync(tokens.AccessToken, new
        {
            accountId,
            type = "Split",
            bookedAt = BookedAt,
            security = new[] { new { instrumentId, quantity = 200m, cost = 1m } }
        })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await PostBodyAsync(tokens.AccessToken, new
        {
            accountId,
            type = "Split",
            bookedAt = BookedAt,
            security = new[] { new { instrumentId, quantity = 200m, price = 1m } }
        })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var holding = await HoldingAsync(tokens.AccessToken, accountId, instrumentId);
        holding.Quantity.ShouldBe(100m);
        holding.Cost.ShouldBe(2500m);
    }

    [Test]
    public async Task DisabledInstrument_Returns400()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInTenantAdmin();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Brokerage", "USD");
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken, "VTI", "USD");
        (await PostOpeningAsync(tokens.AccessToken, accountId, instrumentId, 100m, 2500m)).StatusCode
            .ShouldBe(HttpStatusCode.Created);
        await DisableInstrumentAsync(instrumentId, tokens.AccessToken);

        (await PostSplitAsync(tokens.AccessToken, accountId, "Split", instrumentId, 200m)).StatusCode
            .ShouldBe(HttpStatusCode.BadRequest);

        var holding = await HoldingAsync(tokens.AccessToken, accountId, instrumentId);
        holding.Quantity.ShouldBe(100m);
        holding.Cost.ShouldBe(2500m);
    }

    [Test]
    public async Task IdempotencyKey_ReplaysTheSplitAndRejectsADifferentQuantity()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Brokerage", "USD");
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken, "VTI", "USD");
        (await PostOpeningAsync(tokens.AccessToken, accountId, instrumentId, 100m, 2500m)).StatusCode
            .ShouldBe(HttpStatusCode.Created);
        var key = Guid.NewGuid();

        var id = await ReadId(await PostSplitAsync(tokens.AccessToken, accountId, "Split", instrumentId, 200m, key));
        var replay = await PostSplitAsync(tokens.AccessToken, accountId, "Split", instrumentId, 200m, key);
        replay.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await ReadId(replay)).ShouldBe(id);

        var mismatch = await PostSplitAsync(tokens.AccessToken, accountId, "Split", instrumentId, 300m, key);
        mismatch.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var holding = await HoldingAsync(tokens.AccessToken, accountId, instrumentId);
        holding.Quantity.ShouldBe(200m);
        holding.Cost.ShouldBe(2500m);
    }

    [Test]
    public async Task OtherTenantAccountOrInstrument_Returns404()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Brokerage", "USD");
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken, "VTI", "USD");
        (await PostOpeningAsync(tokens.AccessToken, accountId, instrumentId, 100m, 2500m)).StatusCode
            .ShouldBe(HttpStatusCode.Created);

        var (_, _, southCustomer, southTokens) = await AccountHttp.SignInTenantAdmin(
            "South Advisory", "south-advisory", "tess@south.example", adviserEmail: "ann@south.example",
            customerEmail: "ned@south.example");
        var southAccount = await OpenAccountAsync(southTokens.AccessToken, southCustomer.PublicId, "Brokerage", "USD");
        var southInstrument = await CreateInstrumentAsync(southTokens.AccessToken, "AIA", "USD");

        (await PostSplitAsync(tokens.AccessToken, southAccount, "Split", instrumentId, 200m)).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
        (await PostSplitAsync(tokens.AccessToken, accountId, "Split", southInstrument, 200m)).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);

        var holding = await HoldingAsync(tokens.AccessToken, accountId, instrumentId);
        holding.Quantity.ShouldBe(100m);
        holding.Cost.ShouldBe(2500m);
    }

    private static async Task<Guid> ReadId(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<int> HoldingCountAsync(string accessToken, Guid accountId)
    {
        var response = await AccountHttp.Send(HttpMethod.Get, $"/accounts/{accountId}/holdings", accessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("items").GetArrayLength();
    }

    private static async Task<JsonDocument> GetItemAsync(string accessToken, Guid id)
    {
        var response = await TransactionHttp.Send(HttpMethod.Get, $"/transactions/{id}", accessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
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

    private static Task<HttpResponseMessage> PostOpeningAsync(
        string accessToken,
        Guid accountId,
        Guid instrumentId,
        decimal quantity,
        decimal cost) =>
        TransactionHttp.Send(HttpMethod.Post, "/transactions", accessToken, new
        {
            accountId,
            type = "OpeningHoldings",
            bookedAt = BookedAt,
            security = new[] { new { instrumentId, quantity, cost } }
        }, Guid.NewGuid());

    private static Task<HttpResponseMessage> PostBodyAsync(string accessToken, object body) =>
        TransactionHttp.Send(HttpMethod.Post, "/transactions", accessToken, body, Guid.NewGuid());

    private static Task<HttpResponseMessage> PostSplitAsync(
        string accessToken,
        Guid accountId,
        string type,
        Guid instrumentId,
        decimal quantity,
        Guid? key = null) =>
        TransactionHttp.Send(HttpMethod.Post, "/transactions", accessToken, new
        {
            accountId,
            type,
            bookedAt = BookedAt,
            security = new[] { new { instrumentId, quantity } }
        }, key ?? Guid.NewGuid());

    private static async Task<Guid> OpenAccountAsync(
        string accessToken,
        Guid customerId,
        string type,
        string currency)
    {
        var response = await AccountHttp.Send(HttpMethod.Post, "/accounts", accessToken, new
        {
            customerId,
            name = type + " " + currency + " " + Guid.NewGuid().ToString("N")[..8],
            type,
            currency
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
