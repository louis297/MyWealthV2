using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Instruments;
using MyWealthV2.Application.FunctionalTests.Transactions;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.ValueObjects;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Fees;

/// <summary>HTTP helpers for <c>/fees/trade-schedules</c> and the trades that set the cut-off date (trade-fees R9).</summary>
internal static class FeeHttp
{
    public const string Schedules = "/fees/trade-schedules";

    public static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>
    /// A schedule start in the past, after the funding and "before" trades of the fee tests (October 2026).
    /// A trade cannot be dated in the future (fixes/ledger-write-guards.md Decision 6), so the fee tests
    /// insert their schedule directly from this date instead of creating it after the cut-off over HTTP.
    /// </summary>
    public static readonly DateOnly ScheduleFrom = new(2026, 11, 1);

    /// <summary>A trade instant on which a schedule from <see cref="ScheduleFrom"/> is in effect.</summary>
    public static string TradeAt => At(ScheduleFrom.AddDays(1));

    public static async Task<HttpResponseMessage> Send(
        HttpMethod method,
        string path,
        string? accessToken,
        object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await FunctionalTestSetup.WebClient.SendAsync(request);
    }

    /// <summary>A create body: 0.5% to 10,000 then 0.3%, minimum 10, no cap, from <paramref name="effectiveFrom"/>.</summary>
    public static object ScheduleBody(string quoteCurrency, DateOnly effectiveFrom, Guid? tenantId = null) =>
        tenantId is null
            ? new
            {
                quoteCurrency,
                effectiveFrom = effectiveFrom.ToString("yyyy-MM-dd"),
                minimum = 10.00m,
                cap = (decimal?)null,
                bands = new[] { new { from = 0m, ratePercent = 0.5m }, new { from = 10000.00m, ratePercent = 0.3m } }
            }
            : new
            {
                tenantId,
                quoteCurrency,
                effectiveFrom = effectiveFrom.ToString("yyyy-MM-dd"),
                minimum = 10.00m,
                cap = (decimal?)null,
                bands = new[] { new { from = 0m, ratePercent = 0.5m }, new { from = 10000.00m, ratePercent = 0.3m } }
            };

    public static async Task<Guid> CreateScheduleAsync(
        string accessToken,
        string quoteCurrency,
        DateOnly effectiveFrom,
        Guid? tenantId = null)
    {
        var response = await Send(HttpMethod.Post, Schedules, accessToken, ScheduleBody(quoteCurrency, effectiveFrom, tenantId));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>
    /// Inserts a schedule directly, as the API only accepts dates after the cut-off (trade-fees R9).
    /// Default: 0.5% to 10,000 then 0.3%, minimum 10, no cap.
    /// </summary>
    public static async Task<Guid> AddScheduleAsync(
        int tenantId,
        string currency,
        DateOnly effectiveFrom,
        decimal minimum = 10.00m,
        params FeeBand[] bands)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var catalogCurrency = await db.Set<Currency>().AsNoTracking().SingleAsync(row => row.Code == currency);
        var schedule = TradeFeeSchedule.Create(
            tenantId,
            catalogCurrency,
            effectiveFrom,
            minimum,
            null,
            bands.Length == 0 ? [new FeeBand(0m, 0.5m), new FeeBand(10000.00m, 0.3m)] : bands,
            effectiveFrom.AddDays(-1));
        schedule.CreatedBy = "test";
        db.TradeFeeSchedules.Add(schedule);
        await db.SaveChangesAsync();
        return schedule.PublicId;
    }

    public static async Task<Guid> CreateScheduleAsync(string accessToken, object body)
    {
        var response = await Send(HttpMethod.Post, Schedules, accessToken, body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    public static async Task<JsonDocument> GetScheduleAsync(string accessToken, Guid id)
    {
        var response = await Send(HttpMethod.Get, $"{Schedules}/{id}", accessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    public static async Task<string> RowVersionAsync(string accessToken, Guid id)
    {
        using var item = await GetScheduleAsync(accessToken, id);
        return item.RootElement.GetProperty("rowVersion").GetString()!;
    }

    public static async Task<Guid> OpenAccountAsync(string accessToken, Guid customerId, string type, string currency)
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

    public static async Task<Guid> CreateInstrumentAsync(string accessToken, string symbol, string quoteCurrency)
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

    public static async Task SetPriceAsync(Guid instrumentPublicId, decimal amount, string currency)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await TestPrices.UpsertAsync(db, instrumentPublicId, amount, currency);
    }

    public static Task<HttpResponseMessage> PostCashAsync(
        string accessToken,
        Guid accountId,
        decimal amount,
        string bookedAt = "2026-10-11T10:00:00Z") =>
        TransactionHttp.Send(HttpMethod.Post, "/transactions", accessToken, new
        {
            accountId,
            type = "TransferIn",
            amount,
            bookedAt
        }, Guid.NewGuid());

    public static Task<HttpResponseMessage> PostTradeAsync(
        string accessToken,
        Guid accountId,
        string type,
        Guid instrumentId,
        decimal quantity,
        string bookedAt) =>
        TransactionHttp.Send(HttpMethod.Post, "/transactions", accessToken, new
        {
            accountId,
            type,
            bookedAt,
            security = new[] { new { instrumentId, quantity } }
        }, Guid.NewGuid());

    /// <summary>A funded USD brokerage Account with a USD instrument priced at 25.00.</summary>
    public static async Task<(Guid AccountId, Guid InstrumentId)> TradingAccountAsync(
        string accessToken,
        Guid customerId,
        string symbol = "VTI",
        string currency = "USD",
        decimal cash = 100000m)
    {
        var accountId = await OpenAccountAsync(accessToken, customerId, "Brokerage", currency);
        var instrumentId = await CreateInstrumentAsync(accessToken, symbol, currency);
        await SetPriceAsync(instrumentId, 25.00m, currency);
        (await PostCashAsync(accessToken, accountId, cash)).StatusCode.ShouldBe(HttpStatusCode.Created);
        return (accountId, instrumentId);
    }

    /// <summary>
    /// Inserts a Buy at 25.00 directly, without a fee, as a row posted before a future bookedAt was refused
    /// (fixes/ledger-write-guards.md Decision 9). Only the cut-off date reads it; the holding is not projected.
    /// </summary>
    public static async Task InsertBuyAsync(Guid accountPublicId, Guid instrumentPublicId, decimal quantity, DateOnly tradeDate)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var account = await db.Accounts.SingleAsync(row => row.PublicId == accountPublicId);
        var instrument = await db.Instruments.SingleAsync(row => row.PublicId == instrumentPublicId);
        var cash = await db.TransactionCashLegs.Where(leg => leg.AccountId == account.Id).SumAsync(leg => leg.Amount);
        var buy = Transaction.PostBuy(
            account.TenantId,
            account.Id,
            account.Type,
            account.Currency,
            instrument.Id,
            quantity,
            Money.Create(25.00m, instrument.QuoteCurrency.Trim()),
            fxRate: 1m,
            DateTimeOffset.Parse(At(tradeDate)),
            memo: null,
            reference: null,
            currentCashSum: cash);
        buy.CreatedBy = "test";
        buy.CashLegs.ForEach(leg => leg.CreatedBy = "test");
        buy.SecurityLegs.ForEach(leg => leg.CreatedBy = "test");
        db.Transactions.Add(buy);
        await db.SaveChangesAsync();
    }

    /// <summary>An instant at 10:00 UTC on <paramref name="date"/>.</summary>
    public static string At(DateOnly date) => date.ToString("yyyy-MM-dd") + "T10:00:00Z";

    public static async Task<Dictionary<string, string[]>> ErrorsAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (json.RootElement.TryGetProperty("errors", out var errors))
        {
            foreach (var property in errors.EnumerateObject())
            {
                result[property.Name] = property.Value.EnumerateArray().Select(value => value.GetString()!).ToArray();
            }
        }

        return result;
    }
}
