using System.Text.Json;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.Reports;

public static class ReportExecutionVisibility
{
    public static bool TryReadParameters(string json, out JsonElement parameters)
    {
        try
        {
            parameters = JsonSerializer.Deserialize<JsonElement>(json);
        }
        catch (JsonException)
        {
            parameters = default;
            return false;
        }

        return parameters.ValueKind == JsonValueKind.Object;
    }

    /// <summary>
    /// Whether the caller can see an execution whose report scope they can read.
    /// <paramref name="customerAdvisers"/> maps Customer PublicId to the assigned Adviser's Person id.
    /// </summary>
    public static bool IsVisible(
        string scope,
        JsonElement parameters,
        UserRole role,
        ICurrentUser currentUser,
        IReadOnlyDictionary<Guid, int?> customerAdvisers)
    {
        if (role == UserRole.TenantAdmin)
        {
            return true;
        }

        if (scope == "Book")
        {
            return role == UserRole.Adviser
                && parameters.TryGetProperty("adviserId", out var adviserId)
                && adviserId.TryGetGuid(out var id)
                && id == currentUser.PublicId;
        }

        if (!TryReadCustomerId(parameters, out var customerPublicId))
        {
            return false;
        }

        if (role == UserRole.Customer)
        {
            return customerPublicId == currentUser.PublicId;
        }

        return role == UserRole.Adviser
            && customerAdvisers.TryGetValue(customerPublicId, out var adviserPersonId)
            && adviserPersonId == currentUser.Person?.Id;
    }

    public static bool TryReadCustomerId(JsonElement parameters, out Guid customerId)
    {
        customerId = default;
        return parameters.TryGetProperty("customerId", out var value) && value.TryGetGuid(out customerId);
    }
}
