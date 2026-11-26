using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Data.Entities;

namespace MyWealthV2.Application.FunctionalTests.Valuations;

/// <summary>Helpers for the account valuation functional tests (docs/features/account-valuation.md §10).</summary>
internal static class ValuationHttp
{
    public static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    public static Task<HttpResponseMessage> GetAsync(string? accessToken, Guid accountId, string? date) =>
        AccountHttp.Send(
            HttpMethod.Get,
            date is null ? $"/accounts/{accountId}/valuation" : $"/accounts/{accountId}/valuation?date={Uri.EscapeDataString(date)}",
            accessToken);

    /// <summary>The valuation of <paramref name="day"/>, asserted 200.</summary>
    public static async Task<JsonElement> ReadAsync(string accessToken, Guid accountId, DateOnly day)
    {
        var response = await GetAsync(accessToken, accountId, day.ToString("yyyy-MM-dd"));
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.Clone();
    }

    public static JsonElement Holding(JsonElement valuation, string symbol) =>
        valuation.GetProperty("holdings").EnumerateArray().Single(line => line.GetProperty("symbol").GetString() == symbol);

    public static async Task AddPriceAsync(Guid instrumentId, string currency, DateOnly date, decimal price)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await TestPrices.UpsertAsync(db, instrumentId, price, currency, date);
    }

    public static async Task AddRateAsync(string baseCurrency, string quoteCurrency, DateOnly date, decimal rate)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await db.FxRates.SingleOrDefaultAsync(existing =>
            existing.BaseCurrency == baseCurrency && existing.QuoteCurrency == quoteCurrency && existing.AsOfDate == date);
        if (row is null)
        {
            row = new FxRate { BaseCurrency = baseCurrency, QuoteCurrency = quoteCurrency, AsOfDate = date };
            db.FxRates.Add(row);
        }

        row.Rate = rate;
        row.Source = "Frankfurter";
        row.FetchedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
    }

    /// <summary>Moves one Transaction to another <c>bookedAt</c>: a Reversal, or a posting on a Custodial account, is
    /// always booked at the server's time.</summary>
    public static async Task SetBookedAtAsync(Guid transactionId, DateTimeOffset bookedAt)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var utc = bookedAt.ToUniversalTime();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [Transactions] SET [BookedAt] = {utc} WHERE [PublicId] = {transactionId}");
    }

    /// <summary>Posts a body to <c>POST /transactions</c> and returns the new id.</summary>
    public static async Task<Guid> PostAsync(string accessToken, Dictionary<string, object?> body) =>
        await CustodialHttp.ReadIdAsync(await CustodialHttp.PostAsync(accessToken, body));

    public static Dictionary<string, object?> Cash(Guid accountId, string type, decimal amount, string bookedAt) => new()
    {
        ["accountId"] = accountId,
        ["type"] = type,
        ["amount"] = amount,
        ["bookedAt"] = bookedAt
    };

    public static Dictionary<string, object?> Opening(Guid accountId, string bookedAt, params (Guid InstrumentId, decimal Quantity, decimal Cost)[] legs) => new()
    {
        ["accountId"] = accountId,
        ["type"] = "OpeningHoldings",
        ["bookedAt"] = bookedAt,
        ["security"] = legs.Select(leg => new { instrumentId = leg.InstrumentId, quantity = leg.Quantity, cost = leg.Cost }).ToArray()
    };

    public static Dictionary<string, object?> Trade(
        Guid accountId,
        string type,
        Guid instrumentId,
        decimal quantity,
        decimal price,
        string bookedAt,
        decimal? fee = null,
        decimal? fxRate = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["accountId"] = accountId,
            ["type"] = type,
            ["bookedAt"] = bookedAt,
            ["security"] = new[] { new { instrumentId, quantity, price } }
        };
        if (fee is not null)
        {
            body["fee"] = fee;
        }

        if (fxRate is not null)
        {
            body["fxRate"] = fxRate;
        }

        return body;
    }

    /// <summary>A hand <c>Split</c> on an External account: the quantity is the new total (split R9).</summary>
    public static Dictionary<string, object?> Split(Guid accountId, Guid instrumentId, decimal newTotal, string bookedAt) => new()
    {
        ["accountId"] = accountId,
        ["type"] = "Split",
        ["bookedAt"] = bookedAt,
        ["security"] = new[] { new { instrumentId, quantity = newTotal } }
    };
}
