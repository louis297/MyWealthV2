using System.Text.Json;

namespace MyWealthV2.Application.FunctionalTests.Infrastructure;

/// <summary>Reads the RFC 9110 problem body that api-design §4.2 defines.</summary>
public sealed record ProblemResponse(string? Code, string? Title, string? Detail)
{
    public static async Task<ProblemResponse> ReadAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        return new ProblemResponse(Read(root, "code"), Read(root, "title"), Read(root, "detail"));
    }

    private static string? Read(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
