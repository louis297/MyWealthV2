using System.Net;
using System.Text.Json;
using MyWealthV2.Simulator.Tests.Control;

namespace MyWealthV2.Simulator.Tests.Market;

internal static class MarketHttp
{
    public static Task<HttpResponseMessage> AddAsync(
        HttpClient control,
        string symbol,
        string currency = "USD",
        decimal price = 100m,
        decimal? dailyMovePercent = null,
        string name = "Acme Simulated Holdings") =>
        control.PostJsonAsync("/control/market/instruments",
            JsonSerializer.Serialize(new { symbol, name, currency, price, dailyMovePercent }));

    public static async Task AddOkAsync(
        HttpClient control,
        string symbol,
        string currency = "USD",
        decimal price = 100m,
        decimal? dailyMovePercent = null)
    {
        var response = await AddAsync(control, symbol, currency, price, dailyMovePercent);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    public static Task<HttpResponseMessage> SetPriceAsync(HttpClient control, string symbol, decimal price) =>
        control.PostJsonAsync($"/control/market/instruments/{symbol}/price", JsonSerializer.Serialize(new { price }));

    public static Task<HttpResponseMessage> SetDailyMoveAsync(HttpClient control, string symbol, decimal dailyMovePercent) =>
        control.PostJsonAsync($"/control/market/instruments/{symbol}/daily-move", JsonSerializer.Serialize(new { dailyMovePercent }));

    public static async Task AdvanceAsync(HttpClient control, string by)
    {
        var response = await control.PostJsonAsync("/control/clock/advance", JsonSerializer.Serialize(new { by }));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    public static async Task<JsonElement> InstrumentsAsync(HttpClient control)
    {
        var response = await control.GetAsync("/control/market/instruments");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.Clone();
    }

    /// <summary>The prices of <paramref name="symbol"/>, newest day first: (date, price, set).</summary>
    public static async Task<List<(DateOnly Date, decimal Price, bool Set)>> PricesAsync(HttpClient control, string symbol)
    {
        var response = await control.GetAsync($"/control/market/instruments/{symbol}/prices?pageSize=100");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => (
                DateOnly.Parse(item.GetProperty("date").GetString()!),
                item.GetProperty("price").GetDecimal(),
                item.GetProperty("set").GetBoolean()))
            .ToList();
    }

    // The market tables exist from script 0008 and its corporate actions from 0009; until then the reset leaves them alone.
    public const string ResetMarket = """
        IF OBJECT_ID(N'dbo.CustodianDividendPayments', N'U') IS NOT NULL EXEC(N'DELETE FROM [CustodianDividendPayments]');
        IF OBJECT_ID(N'dbo.MarketCorporateActionVersions', N'U') IS NOT NULL EXEC(N'DELETE FROM [MarketCorporateActionVersions]');
        IF OBJECT_ID(N'dbo.MarketCorporateActions', N'U') IS NOT NULL EXEC(N'DELETE FROM [MarketCorporateActions]');
        IF OBJECT_ID(N'dbo.MarketPrices', N'U') IS NOT NULL EXEC(N'DELETE FROM [MarketPrices]');
        IF OBJECT_ID(N'dbo.MarketInstruments', N'U') IS NOT NULL EXEC(N'DELETE FROM [MarketInstruments]');
        DELETE FROM [ControlLog];
        UPDATE [World] SET [ClockOffsetSeconds] = 0, [PausedAt] = NULL, [PartyOutage] = 0;
        """;

    public static Task<HttpResponseMessage> AnnounceAsync(
        HttpClient control,
        string symbol = "ACME.SIM",
        string kind = "Split",
        int ratioNew = 2,
        int ratioHeld = 1,
        DateOnly? exDate = null,
        DateOnly? recordDate = null) =>
        control.PostJsonAsync("/control/market/corporate-actions", JsonSerializer.Serialize(new
        {
            symbol,
            kind,
            ratioNew,
            ratioHeld,
            exDate = (exDate ?? new DateOnly(2026, 11, 21)).ToString("yyyy-MM-dd"),
            recordDate = recordDate?.ToString("yyyy-MM-dd")
        }));

    /// <summary>Announces and answers the party action id.</summary>
    public static async Task<string> AnnounceOkAsync(
        HttpClient control,
        string symbol = "ACME.SIM",
        string kind = "Split",
        int ratioNew = 2,
        int ratioHeld = 1,
        DateOnly? exDate = null)
    {
        var response = await AnnounceAsync(control, symbol, kind, ratioNew, ratioHeld, exDate);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.GetProperty("id").GetString()!;
    }

    public static async Task<JsonElement> CorporateActionAsync(HttpClient control, string id)
    {
        var response = await control.GetAsync($"/control/market/corporate-actions/{id}");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.Clone();
    }

    public static async Task<Guid> WorldIdAsync()
    {
        var rows = await Sql.ReadAsync(SimulatorTestSetup.ConnectionString, "SELECT [WorldId] FROM [World]");
        return (Guid)rows.Single()[0];
    }
}
