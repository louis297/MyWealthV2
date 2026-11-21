using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.Accounts;

namespace MyWealthV2.Application.FunctionalTests.BankReconciliations;

/// <summary>The <c>/bank-reconciliations</c> routes (docs/features/bank-reconciliation.md §8).</summary>
internal static class BankReconciliationHttp
{
    public const string Path = "/bank-reconciliations";

    public static Task<HttpResponseMessage> Send(HttpMethod method, string path, string? accessToken, object? body = null) =>
        AccountHttp.Send(method, path, accessToken, body);

    public static Task<HttpResponseMessage> PostAsync(string? accessToken, object body) =>
        Send(HttpMethod.Post, Path, accessToken, body);

    /// <summary>"Run now" (R17); answers the new run's id.</summary>
    public static async Task<Guid> RunAsync(string accessToken, Guid clientMoneyAccountId)
    {
        var response = await PostAsync(accessToken, new { clientMoneyAccountId });
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>Runs now and reads the run back (R17, R21).</summary>
    public static async Task<JsonElement> RunAndGetAsync(string accessToken, Guid clientMoneyAccountId) =>
        await GetAsync(accessToken, await RunAsync(accessToken, clientMoneyAccountId));

    public static async Task<JsonElement> GetAsync(string accessToken, Guid id) => await ReadAsync(accessToken, $"{Path}/{id}");

    public static async Task<JsonElement> ListAsync(string accessToken, string query = "") => await ReadAsync(accessToken, Path + query);

    public static async Task<JsonElement> OverviewAsync(string accessToken) => await ReadAsync(accessToken, $"{Path}/overview");

    /// <summary>The items of a run as (kind, class, amount, count).</summary>
    public static List<(string Kind, string Class, decimal Amount, int Count)> Items(JsonElement run) =>
        run.GetProperty("items").EnumerateArray()
            .Select(item => (
                item.GetProperty("kind").GetString()!,
                item.GetProperty("class").GetString()!,
                item.GetProperty("amount").GetDecimal(),
                item.GetProperty("count").GetInt32()))
            .ToList();

    public static string Status(JsonElement run) => run.GetProperty("status").GetString()!;

    public static decimal? Decimal(JsonElement run, string name) =>
        run.GetProperty(name).ValueKind == JsonValueKind.Null ? null : run.GetProperty(name).GetDecimal();

    /// <summary>Every route under <c>/bank-reconciliations</c>, with a body for the POST.</summary>
    public static IEnumerable<(HttpMethod Method, string Route, object? Body)> Routes(Guid id, Guid clientMoneyAccountId)
    {
        yield return (HttpMethod.Get, $"{Path}/overview", null);
        yield return (HttpMethod.Get, Path, null);
        yield return (HttpMethod.Get, $"{Path}/{id}", null);
        yield return (HttpMethod.Post, Path, new { clientMoneyAccountId });
    }

    private static async Task<JsonElement> ReadAsync(string accessToken, string path)
    {
        var response = await Send(HttpMethod.Get, path, accessToken);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.Clone();
    }
}
