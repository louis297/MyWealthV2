using System.Net;
using System.Text.Json;
using MyWealthV2.Simulator.Tests.Control;
using MyWealthV2.Simulator.Worlds;

namespace MyWealthV2.Simulator.Tests.Broker;

/// <summary>The simulator broker's routes and its tables (docs/features/orders.md R52–R65).</summary>
internal static class BrokerHttp
{
    // The broker tables exist from script 0010; until then the reset leaves them alone.
    public const string ResetBroker = """
        IF OBJECT_ID(N'dbo.BrokerReports', N'U') IS NOT NULL
            EXEC(N'DELETE FROM [BrokerReports]; DELETE FROM [BrokerFills]; DELETE FROM [BrokerOrders];
                   UPDATE [BrokerSettings] SET [Mode] = 0, [FillPercent] = 100, [RejectReason] = NULL, [SlippagePercent] = 0.10,
                       [SettlementDays] = 2;');
        """;

    /// <summary>A market day order: a buy of 10 <c>VTI</c> in USD at a limit of 105.00 and a reference price of 100.00.</summary>
    public static string Order(
        string clientOrderId = "2c0c8e1e-7c7c-4d55-9b53-0b8f5d3f6a11",
        string symbol = "VTI",
        string currency = "USD",
        string side = "Buy",
        decimal quantity = 10m,
        decimal priceLimit = 105m,
        decimal referencePrice = 100m,
        string type = "Market",
        string timeInForce = "Day") =>
        JsonSerializer.Serialize(new { clientOrderId, symbol, currency, side, quantity, type, timeInForce, priceLimit, referencePrice });

    public static Task<HttpResponseMessage> PlaceAsync(HttpClient party, string body) =>
        party.PostJsonAsync("/party/broker/orders", body);

    /// <summary>Places an order the broker accepts and answers the broker's order id.</summary>
    public static async Task<string> PlaceOkAsync(HttpClient party, string body)
    {
        var response = await PlaceAsync(party, body);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        json.RootElement.GetProperty("status").GetString().ShouldBe("Accepted", content);
        return json.RootElement.GetProperty("brokerOrderId").GetString()!;
    }

    public static Task<HttpResponseMessage> SetModeAsync(HttpClient control, string? mode, int? fillPercent = null, string? reason = null) =>
        control.PostJsonAsync("/control/broker/order-mode", JsonSerializer.Serialize(new { mode, fillPercent, reason }));

    public static async Task SetModeOkAsync(HttpClient control, string mode, int? fillPercent = null, string? reason = null)
    {
        var response = await SetModeAsync(control, mode, fillPercent, reason);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    public static Task<HttpResponseMessage> SetSlippageAsync(HttpClient control, decimal? slippagePercent) =>
        control.PostJsonAsync("/control/broker/slippage", JsonSerializer.Serialize(new { slippagePercent }));

    public static async Task SetSlippageOkAsync(HttpClient control, decimal slippagePercent)
    {
        var response = await SetSlippageAsync(control, slippagePercent);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    /// <summary>The broker's orders, oldest first. Status: 0 Working, 1 Filled, 2 Cancelled, 3 Expired, 4 Rejected.</summary>
    public static async Task<List<OrderRow>> OrdersAsync()
    {
        var rows = await Sql.ReadAsync(SimulatorTestSetup.ConnectionString, """
            SELECT [Number], [ClientOrderId], [Status], [FilledQuantity], [AcceptedDay], [RejectReason], [Symbol], [Currency], [Side],
                [Quantity], [PriceLimit], [ReferencePrice]
            FROM [BrokerOrders] ORDER BY [Number]
            """);
        return rows.Select(row => new OrderRow(
            (int)row[0], (string)row[1], (int)row[2], (decimal)row[3], row[4] is DBNull ? null : DateOnly.FromDateTime((DateTime)row[4]),
            row[5] is DBNull ? null : (int)row[5], (string)row[6], (string)row[7], (int)row[8], (decimal)row[9], (decimal)row[10],
            (decimal)row[11])).ToList();
    }

    /// <summary>The broker's fills, oldest first.</summary>
    public static async Task<List<FillRow>> FillsAsync()
    {
        var rows = await Sql.ReadAsync(SimulatorTestSetup.ConnectionString, """
            SELECT f.[Number], o.[Number], f.[Quantity], f.[Price], f.[TradeDate], f.[SettlementDate], f.[ContractNote], f.[Settled]
            FROM [BrokerFills] f JOIN [BrokerOrders] o ON o.[Id] = f.[OrderId] ORDER BY f.[Number]
            """);
        return rows.Select(row => new FillRow(
            (int)row[0], (int)row[1], (decimal)row[2], (decimal)row[3], DateOnly.FromDateTime((DateTime)row[4]),
            DateOnly.FromDateTime((DateTime)row[5]), (string)row[6], (bool)row[7])).ToList();
    }

    /// <summary>The broker's reports in feed order. Kind: 0 Fill, 1 Cancelled, 2 Expired, 3 Settled.</summary>
    public static async Task<List<ReportRow>> ReportsAsync()
    {
        var rows = await Sql.ReadAsync(SimulatorTestSetup.ConnectionString, """
            SELECT r.[Kind], o.[Number], f.[Number], r.[SettledDate], r.[Reason]
            FROM [BrokerReports] r JOIN [BrokerOrders] o ON o.[Id] = r.[OrderId] LEFT JOIN [BrokerFills] f ON f.[Id] = r.[FillId]
            ORDER BY r.[Id]
            """);
        return rows.Select(row => new ReportRow(
            (int)row[0], (int)row[1], row[2] is DBNull ? null : (int)row[2],
            row[3] is DBNull ? null : DateOnly.FromDateTime((DateTime)row[3]), row[4] is DBNull ? null : (string)row[4])).ToList();
    }

    /// <summary>R56: the fill price of fill <paramref name="n"/> of a world with <paramref name="seed"/>.</summary>
    public static decimal FillPrice(decimal basePrice, decimal slippagePercent, int seed, int n)
    {
        var u = (decimal)SeededRandom.For(seed, "broker-fill", n).NextDouble();
        var slip = slippagePercent / 100m * (2m * u - 1m);
        return Math.Max(0.0001m, Math.Round(basePrice * (1m + slip), 4, MidpointRounding.AwayFromZero));
    }

    public static async Task<List<string>> LogActionsAsync()
    {
        var rows = await Sql.ReadAsync(SimulatorTestSetup.ConnectionString, "SELECT [Action] FROM [ControlLog] ORDER BY [Id]");
        return rows.Select(row => (string)row[0]).ToList();
    }
}

internal sealed record OrderRow(
    int Number,
    string ClientOrderId,
    int Status,
    decimal FilledQuantity,
    DateOnly? AcceptedDay,
    int? RejectReason,
    string Symbol,
    string Currency,
    int Side,
    decimal Quantity,
    decimal PriceLimit,
    decimal ReferencePrice);

internal sealed record FillRow(
    int Number,
    int OrderNumber,
    decimal Quantity,
    decimal Price,
    DateOnly TradeDate,
    DateOnly SettlementDate,
    string ContractNote,
    bool Settled);

internal sealed record ReportRow(int Kind, int OrderNumber, int? FillNumber, DateOnly? SettledDate, string? Reason);
