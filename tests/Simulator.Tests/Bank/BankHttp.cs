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

    public const string ResetBank = """
        DELETE FROM [BankFeedPlaces];
        DELETE FROM [BankLines];
        DELETE FROM [BankAccounts];
        DELETE FROM [ControlLog];
        UPDATE [World] SET [ClockOffsetSeconds] = 0, [PausedAt] = NULL, [PartyOutage] = 0, [NextLineNumber] = 1;
        """;
}

internal sealed record FeedPage(IReadOnlyList<JsonElement> Items, string Cursor, bool HasMore)
{
    public IReadOnlyList<string> Ids => Items.Select(item => item.GetProperty("id").GetString()!).ToList();
}
