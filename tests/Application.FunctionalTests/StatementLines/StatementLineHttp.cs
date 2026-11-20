using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.Accounts;

namespace MyWealthV2.Application.FunctionalTests.StatementLines;

/// <summary>The <c>/statement-lines</c> routes (docs/features/deposit-matching.md §8).</summary>
internal static class StatementLineHttp
{
    public const string Path = "/statement-lines";

    public static Task<HttpResponseMessage> Send(HttpMethod method, string path, string? accessToken, object? body = null) =>
        AccountHttp.Send(method, path, accessToken, body);

    public static async Task<JsonElement> ListAsync(string accessToken, string query = "")
    {
        var response = await Send(HttpMethod.Get, Path + query, accessToken);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.Clone();
    }

    public static async Task<List<Guid>> ListIdsAsync(string accessToken, string query = "") =>
        (await ListAsync(accessToken, query)).GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .ToList();

    public static async Task<JsonElement> GetAsync(string accessToken, Guid id)
    {
        var response = await Send(HttpMethod.Get, $"{Path}/{id}", accessToken);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.Clone();
    }

    public static async Task<string> RowVersionAsync(string accessToken, Guid id) =>
        (await GetAsync(accessToken, id)).GetProperty("rowVersion").GetString()!;

    /// <summary>Every route under <c>/statement-lines</c>, with a body each action accepts.</summary>
    public static IEnumerable<(HttpMethod Method, string Route, object? Body)> Routes(Guid id)
    {
        yield return (HttpMethod.Get, Path, null);
        yield return (HttpMethod.Get, $"{Path}/{id}", null);
    }
}
