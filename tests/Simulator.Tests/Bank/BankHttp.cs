using System.Net;
using System.Text.Json;
using MyWealthV2.Simulator.Tests.Control;

namespace MyWealthV2.Simulator.Tests.Bank;

/// <summary>The simulator bank's control and party routes (docs/features/deposit-matching.md §4.6).</summary>
internal static class BankHttp
{
    public static Task<HttpResponseMessage> OpenAsync(HttpClient control, string accountNumber, string currency = "USD", string name = "Trust") =>
        control.PostJsonAsync("/control/bank/accounts", JsonSerializer.Serialize(new { accountNumber, currency, name }));

    public static async Task OpenOkAsync(HttpClient control, string accountNumber, string currency = "USD")
    {
        var response = await OpenAsync(control, accountNumber, currency);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    public static Task<HttpResponseMessage> AddReceiptAsync(HttpClient control, object body) =>
        control.PostJsonAsync("/control/bank/receipts", JsonSerializer.Serialize(body));

    public static async Task<string> AddReceiptOkAsync(HttpClient control, object body)
    {
        var response = await AddReceiptAsync(control, body);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.GetProperty("id").GetString()!;
    }

    public static Task<HttpResponseMessage> FeedAsync(HttpClient party, string accountNumber, string? after = null, int? limit = null)
    {
        var query = new List<string>();
        if (after is not null)
        {
            query.Add("after=" + Uri.EscapeDataString(after));
        }

        if (limit is not null)
        {
            query.Add("limit=" + limit);
        }

        var path = $"/party/bank/accounts/{Uri.EscapeDataString(accountNumber)}/statement-lines";
        return party.GetAsync(query.Count == 0 ? path : path + "?" + string.Join("&", query));
    }

    public static async Task<FeedPage> FeedOkAsync(HttpClient party, string accountNumber, string? after = null, int? limit = null)
    {
        var response = await FeedAsync(party, accountNumber, after, limit);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        var root = json.RootElement;
        return new FeedPage(
            root.GetProperty("items").EnumerateArray().Select(item => item.Clone()).ToList(),
            root.GetProperty("cursor").GetString()!,
            root.GetProperty("hasMore").GetBoolean());
    }

    public static async Task<JsonElement> AccountsAsync(HttpClient control)
    {
        var response = await control.GetAsync("/control/bank/accounts");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.Clone();
    }

    public static Task<HttpResponseMessage> PayAsync(HttpClient party, string accountNumber, object body) =>
        party.PostJsonAsync($"/party/bank/accounts/{Uri.EscapeDataString(accountNumber)}/payments", JsonSerializer.Serialize(body));

    /// <summary>A payment body by withdrawals R49, with <paramref name="instructionId"/> fresh when not given.</summary>
    public static Dictionary<string, object?> Payment(string? instructionId = null, decimal amount = 300.00m) => new()
    {
        ["instructionId"] = instructionId ?? Guid.NewGuid().ToString(),
        ["amount"] = amount,
        ["currency"] = "USD",
        ["reference"] = "K7M3Q9PXB",
        ["payee"] = new Dictionary<string, object?> { ["accountNumber"] = "06-0123-0456789-00", ["name"] = "J Smith" }
    };

    public static Task<HttpResponseMessage> SetModeAsync(HttpClient control, string mode, string? reason = null) =>
        control.PostJsonAsync("/control/bank/payment-mode", JsonSerializer.Serialize(new { mode, reason }));

    public static async Task<JsonElement> PaymentsAsync(HttpClient control)
    {
        var response = await control.GetAsync("/control/bank/payments");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.Clone();
    }

    // The payments tables exist from script 0004; until then the reset leaves them alone.
    public const string ResetBank = """
        IF OBJECT_ID(N'dbo.BankPayments', N'U') IS NOT NULL EXEC(N'DELETE FROM [BankPayments]');
        IF COL_LENGTH(N'dbo.BankLines', N'ReturnOfLineId') IS NOT NULL EXEC(N'UPDATE [BankLines] SET [ReturnOfLineId] = NULL');
        DELETE FROM [BankFeedPlaces];
        DELETE FROM [BankLines];
        DELETE FROM [BankAccounts];
        DELETE FROM [ControlLog];
        UPDATE [World] SET [ClockOffsetSeconds] = 0, [PausedAt] = NULL, [PartyOutage] = 0, [NextLineNumber] = 1;
        IF COL_LENGTH(N'dbo.World', N'BankPaymentMode') IS NOT NULL
            EXEC(N'UPDATE [World] SET [BankPaymentMode] = 0, [BankPaymentRejectReason] = NULL, [NextPaymentNumber] = 1');
        """;
}

internal sealed record FeedPage(IReadOnlyList<JsonElement> Items, string Cursor, bool HasMore)
{
    public IReadOnlyList<string> Ids => Items.Select(item => item.GetProperty("id").GetString()!).ToList();
}
