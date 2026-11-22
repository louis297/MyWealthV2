using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.Accounts;

namespace MyWealthV2.Application.FunctionalTests.Interest;

/// <summary>The tax profile routes (docs/features/interest-run.md §4.2, §8).</summary>
internal static class TaxProfileHttp
{
    public const string MePath = "/users/me/tax-profile";
    public const string StaleRowVersion = "AAAAAAAAAAE=";

    public static string Path(Guid customerId) => $"/users/customers/{customerId}/tax-profile";

    public static Task<HttpResponseMessage> Send(HttpMethod method, string path, string? accessToken, object? body = null) =>
        AccountHttp.Send(method, path, accessToken, body);

    public static Dictionary<string, object?> Body(
        string taxResidency = "NewZealand",
        bool irdNumberHeld = true,
        decimal? rwtRatePercent = 17.5m,
        string? rowVersion = null) => new()
    {
        ["taxResidency"] = taxResidency,
        ["irdNumberHeld"] = irdNumberHeld,
        ["rwtRatePercent"] = rwtRatePercent,
        ["rowVersion"] = rowVersion
    };

    public static Task<HttpResponseMessage> PutAsync(string accessToken, Guid customerId, object body) =>
        Send(HttpMethod.Put, Path(customerId), accessToken, body);

    public static async Task<JsonElement> GetAsync(string accessToken, Guid customerId) =>
        await ReadAsync(await Send(HttpMethod.Get, Path(customerId), accessToken));

    public static async Task<JsonElement> GetMineAsync(string accessToken) =>
        await ReadAsync(await Send(HttpMethod.Get, MePath, accessToken));

    /// <summary>Saves the profile with the row version the read returns.</summary>
    public static async Task SetAsync(
        string accessToken,
        Guid customerId,
        string taxResidency = "NewZealand",
        bool irdNumberHeld = true,
        decimal? rwtRatePercent = 17.5m)
    {
        var rowVersion = (await GetAsync(accessToken, customerId)).GetProperty("rowVersion");
        var response = await PutAsync(accessToken, customerId, Body(
            taxResidency, irdNumberHeld, rwtRatePercent, rowVersion.ValueKind == JsonValueKind.Null ? null : rowVersion.GetString()));
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.Clone();
    }
}
