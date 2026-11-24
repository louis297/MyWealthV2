using System.Net;
using System.Text.Json;
using MyWealthV2.Simulator.Tests.Control;

namespace MyWealthV2.Simulator.Tests.Custodian;

/// <summary>The simulator custodian's control and party routes (docs/features/in-kind-transfers.md §4.12).</summary>
internal static class CustodianHttp
{
    public const string Account = "SIM-DEMO-CUSTODY-USD";

    public const string ResetCustodian = """
        IF OBJECT_ID(N'dbo.CustodianTransfers', N'U') IS NOT NULL
            EXEC(N'IF OBJECT_ID(N''dbo.CustodianDividendPayments'', N''U'') IS NOT NULL
                       DELETE FROM [CustodianDividendPayments];
                   IF OBJECT_ID(N''dbo.CustodianRejectedElections'', N''U'') IS NOT NULL
                       DELETE FROM [CustodianRejectedElections];
                   DELETE FROM [CustodianTransfers]; DELETE FROM [CustodianMovements]; DELETE FROM [CustodianAccounts];
                   UPDATE [CustodianSettings] SET [TransferMode] = 0, [RejectReason] = NULL, [RoundingMode] = 0;');
        """;

    public static Task<HttpResponseMessage> OpenAsync(HttpClient control, string accountNumber, string currency = "USD", string name = "Nominees") =>
        control.PostJsonAsync("/control/custodian/accounts", JsonSerializer.Serialize(new { accountNumber, currency, name }));

    public static async Task OpenOkAsync(HttpClient control, string accountNumber = Account, string currency = "USD")
    {
        var response = await OpenAsync(control, accountNumber, currency);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    public static string Transfer(
        string instructionId = "6b1f0c2e-0000-4000-8000-000000000001",
        string direction = "In",
        string symbol = "VTI",
        string currency = "USD",
        decimal quantity = 100m,
        string counterpartyName = "Harbour Brokers") =>
        JsonSerializer.Serialize(new
        {
            instructionId,
            direction,
            symbol,
            currency,
            quantity,
            counterparty = new { name = counterpartyName, account = "HB-0044172", holderName = "J Smith" }
        });

    public static Task<HttpResponseMessage> TransferAsync(HttpClient party, string body, string accountNumber = Account) =>
        party.PostJsonAsync($"/party/custodian/accounts/{Uri.EscapeDataString(accountNumber)}/transfers", body);

    public static async Task<JsonElement> TransferOkAsync(HttpClient party, string body, string accountNumber = Account)
    {
        var response = await TransferAsync(party, body, accountNumber);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.Clone();
    }

    public static async Task<(List<JsonElement> Items, string Cursor, bool HasMore, List<JsonElement> Positions)> FeedAsync(
        HttpClient party, string accountNumber = Account, string? after = null, int? limit = null)
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

        var path = $"/party/custodian/accounts/{Uri.EscapeDataString(accountNumber)}/movements";
        var response = await party.GetAsync(query.Count == 0 ? path : path + "?" + string.Join("&", query));
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        var root = json.RootElement;
        return (root.GetProperty("items").EnumerateArray().Select(item => item.Clone()).ToList(), root.GetProperty("cursor").GetString()!,
            root.GetProperty("hasMore").GetBoolean(), root.GetProperty("positions").EnumerateArray().Select(item => item.Clone()).ToList());
    }

    public static Task<HttpResponseMessage> SetTransferModeAsync(HttpClient control, string mode, string? reason = null) =>
        control.PostJsonAsync("/control/custodian/transfer-mode", JsonSerializer.Serialize(new { mode, reason }));

    public static Task<HttpResponseMessage> SetRoundingModeAsync(HttpClient control, string mode) =>
        control.PostJsonAsync("/control/custodian/rounding-mode", JsonSerializer.Serialize(new { mode }));

    public static Task<HttpResponseMessage> AddMovementAsync(HttpClient control, object body) =>
        control.PostJsonAsync("/control/custodian/movements", JsonSerializer.Serialize(body));

    public static Task<HttpResponseMessage> ExecuteAsync(HttpClient control, string transferId, decimal? quantity = null) =>
        control.PostJsonAsync($"/control/custodian/transfers/{transferId}/execute", JsonSerializer.Serialize(new { quantity }));

    public static async Task<JsonElement> GetAsync(HttpClient control, string path)
    {
        var response = await control.GetAsync(path);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.Clone();
    }

    /// <summary>The account's positions on the control surface, as symbol to quantity.</summary>
    public static async Task<Dictionary<string, decimal>> PositionsAsync(HttpClient control, string accountNumber = Account)
    {
        var accounts = await GetAsync(control, "/control/custodian/accounts");
        var account = accounts.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("accountNumber").GetString() == accountNumber);
        return account.GetProperty("positions").EnumerateArray()
            .ToDictionary(item => item.GetProperty("symbol").GetString()!, item => item.GetProperty("quantity").GetDecimal());
    }
}
