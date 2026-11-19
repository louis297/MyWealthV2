using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace MyWealthV2.Simulator.Tests.Control;

internal static class ControlHttp
{
    public static Task<HttpResponseMessage> PostJsonAsync(this HttpClient client, string path, string json) =>
        client.PostAsync(path, new StringContent(json, Encoding.UTF8, "application/json"));

    public static async Task<WorldItem> ReadWorldAsync(this HttpResponseMessage response)
    {
        var world = await response.Content.ReadFromJsonAsync<WorldItem>();
        return world!;
    }

    public static async Task<JsonDocument> ReadJsonAsync(this HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    public static async Task<IReadOnlyList<string>> ErrorKeysAsync(this HttpResponseMessage response)
    {
        using var json = await response.ReadJsonAsync();
        return json.RootElement.GetProperty("errors").EnumerateObject().Select(property => property.Name).ToList();
    }
}

internal sealed record WorldItem(
    Guid WorldId,
    int Seed,
    DateTimeOffset Now,
    bool Paused,
    bool PartyOutage,
    DateTimeOffset StartedAt);
