using System.Text.Json;

namespace MyWealthV2.BackOffice;

/// <summary>Reads the <c>errors</c> of a 400 validation problem from the platform API, so a page can show each at its field.</summary>
public static class ValidationErrors
{
    /// <summary>
    /// The first message per field of a 400 with <c>errors</c>, keyed by field name without regard to case.
    /// Empty for any other status or body.
    /// </summary>
    public static IReadOnlyDictionary<string, string> From(PlatformApiException exception)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (exception.Status != 400 || string.IsNullOrWhiteSpace(exception.Message))
        {
            return result;
        }

        try
        {
            using var document = JsonDocument.Parse(exception.Message);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("errors", out var errors)
                || errors.ValueKind != JsonValueKind.Object)
            {
                return result;
            }

            foreach (var field in errors.EnumerateObject())
            {
                var first = field.Value.ValueKind == JsonValueKind.Array
                    ? field.Value.EnumerateArray().Select(message => message.GetString()).FirstOrDefault(message => message is not null)
                    : null;
                if (first is not null && !result.ContainsKey(field.Name))
                {
                    result[field.Name] = first;
                }
            }
        }
        catch (JsonException)
        {
            return result;
        }

        return result;
    }
}
