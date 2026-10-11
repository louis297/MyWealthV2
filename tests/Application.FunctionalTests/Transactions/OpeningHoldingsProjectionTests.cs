using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Instruments;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Transactions;

public class OpeningHoldingsProjectionTests : TestBase
{
    private const string BookedAt = "2026-09-27T09:00:00+12:00";

    [Test]
    public async Task Create_UpsertsOneHoldingPerInstrumentEqualToTheLegSums()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var singleAccount = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "One name");
        var vti = await CreateInstrumentAsync(tokens.AccessToken, "VTI", "USD");

        (await PostOpeningAsync(tokens.AccessToken, singleAccount, new[] { Leg(vti, 100m, 1500.00m) }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        (await LoadHoldingsAsync(singleAccount)).ShouldBe([
            (vti, 100m, 1500.00m, "USD")
        ]);

        var twoAccount = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Two names");
        var bnd = await CreateInstrumentAsync(tokens.AccessToken, "BND", "USD");

        (await PostOpeningAsync(
                tokens.AccessToken,
                twoAccount,
                new[] { Leg(bnd, 2m, 0m), Leg(vti, 40m, 600.50m) }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        (await LoadHoldingsAsync(twoAccount)).ShouldBe([
            (bnd, 2m, 0m, "USD"),
            (vti, 40m, 600.50m, "USD")
        ]);
    }

    [Test]
    public async Task Reverse_DeletesHoldingsAndLeavesTheOpeningUntouched()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Reverse book");
        var vti = await CreateInstrumentAsync(tokens.AccessToken, "VTI", "USD");
        var bnd = await CreateInstrumentAsync(tokens.AccessToken, "BND", "USD");
        var openingId = await ReadId(await PostOpeningAsync(
            tokens.AccessToken,
            accountId,
            new[] { Leg(vti, 100m, 1500.00m), Leg(bnd, 2m, 0m) }));

        var response = await ReverseAsync(tokens.AccessToken, openingId);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        created.RootElement.EnumerateObject().Select(property => property.Name).ShouldBe(["id"]);
        var reversalId = created.RootElement.GetProperty("id").GetGuid();
        reversalId.ShouldNotBe(openingId);

        using var original = await GetItemAsync(tokens.AccessToken, openingId);
        original.RootElement.GetProperty("type").GetString().ShouldBe("OpeningHoldings");
        var originalLegs = SecurityByInstrument(original.RootElement);
        originalLegs[vti].GetProperty("quantity").GetDecimal().ShouldBe(100m);
        originalLegs[vti].GetProperty("cost").GetDecimal().ShouldBe(1500.00m);
        originalLegs[bnd].GetProperty("quantity").GetDecimal().ShouldBe(2m);
        originalLegs[bnd].GetProperty("cost").GetDecimal().ShouldBe(0m);

        using var reversal = await GetItemAsync(tokens.AccessToken, reversalId);
        reversal.RootElement.GetProperty("type").GetString().ShouldBe("Reversal");
        reversal.RootElement.GetProperty("originalTransactionId").GetGuid().ShouldBe(openingId);
        AssertNoCashAmount(reversal.RootElement);
        var reversalLegs = SecurityByInstrument(reversal.RootElement);
        reversalLegs[vti].GetProperty("quantity").GetDecimal().ShouldBe(-100m);
        reversalLegs[vti].GetProperty("cost").GetDecimal().ShouldBe(-1500.00m);
        reversalLegs[vti].GetProperty("quoteCurrency").GetString().ShouldBe("USD");
        reversalLegs[bnd].GetProperty("quantity").GetDecimal().ShouldBe(-2m);
        reversalLegs[bnd].GetProperty("cost").GetDecimal().ShouldBe(0m);

        (await LoadHoldingsAsync(accountId)).ShouldBeEmpty();

        (await PostOpeningAsync(tokens.AccessToken, accountId, new[] { Leg(vti, 1m, 5m) }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Reverse_ReplaysTheSameKeyAndConflictsWhenTheKeyIsReused()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Replay book");
        var vti = await CreateInstrumentAsync(tokens.AccessToken, "VTI", "USD");
        var bnd = await CreateInstrumentAsync(tokens.AccessToken, "BND", "USD");
        var first = await ReadId(await PostOpeningAsync(tokens.AccessToken, accountId, new[] { Leg(vti, 10m, 100m) }));
        var second = await ReadId(await PostOpeningAsync(
            tokens.AccessToken, accountId, new[] { Leg(bnd, 3m, 30m) }, "2026-09-27T10:00:00+12:00"));
        var key = Guid.NewGuid();

        (await ReverseAsync(tokens.AccessToken, first, Guid.Empty)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var reversalId = await ReadId(await ReverseAsync(tokens.AccessToken, first, key));
        var replay = await ReverseAsync(tokens.AccessToken, first, key);
        replay.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await ReadId(replay)).ShouldBe(reversalId);

        (await ReverseAsync(tokens.AccessToken, second, key)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await LoadHoldingsAsync(accountId)).ShouldBe([
            (bnd, 3m, 30m, "USD")
        ]);
    }

    private static async Task<List<(Guid InstrumentId, decimal Quantity, decimal Cost, string Currency)>> LoadHoldingsAsync(
        Guid accountPublicId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var rows = await (
            from holding in db.Holdings.AsNoTracking()
            join account in db.Accounts.AsNoTracking() on holding.AccountId equals account.Id
            join instrument in db.Instruments.AsNoTracking() on holding.InstrumentId equals instrument.Id
            where account.PublicId == accountPublicId
            orderby instrument.Symbol
            select new
            {
                instrument.PublicId,
                holding.Quantity,
                holding.CostAmount,
                holding.CostCurrency
            }).ToListAsync();

        return rows
            .Select(row => (row.PublicId, row.Quantity, row.CostAmount, row.CostCurrency.Trim()))
            .ToList();
    }

    private static object Leg(Guid instrumentId, decimal quantity, decimal cost) =>
        new { instrumentId, quantity, cost };

    private static Task<HttpResponseMessage> PostOpeningAsync(
        string accessToken,
        Guid accountId,
        object security,
        string bookedAt = BookedAt) =>
        TransactionHttp.Send(HttpMethod.Post, "/transactions", accessToken, new
        {
            accountId,
            type = "OpeningHoldings",
            bookedAt,
            security
        }, Guid.NewGuid());

    private static Task<HttpResponseMessage> ReverseAsync(string accessToken, Guid id, Guid? key = null)
    {
        Guid? idempotencyKey = key == Guid.Empty ? null : key ?? Guid.NewGuid();
        return TransactionHttp.Send(
            HttpMethod.Post, $"/transactions/{id}/reverse", accessToken, body: null, idempotencyKey);
    }

    private static async Task<Guid> ReadId(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<JsonDocument> GetItemAsync(string accessToken, Guid id)
    {
        var response = await TransactionHttp.Send(HttpMethod.Get, $"/transactions/{id}", accessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static Dictionary<Guid, JsonElement> SecurityByInstrument(JsonElement item) =>
        item.GetProperty("security").EnumerateArray()
            .ToDictionary(leg => leg.GetProperty("instrumentId").GetGuid());

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

    private static async Task<Guid> OpenAccountAsync(string accessToken, Guid customerId, string name)
    {
        var response = await AccountHttp.Send(HttpMethod.Post, "/accounts", accessToken, new
        {
            customerId,
            name,
            type = "Brokerage",
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
}
