using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.Common.Security;

public static class ClientRoleAllowList
{
    private static readonly Dictionary<string, HashSet<UserRole>> Allowed = new(StringComparer.Ordinal)
    {
        ["adviser-portal"] = [UserRole.TenantAdmin, UserRole.Adviser],
        ["back-office"] = [UserRole.SystemAdmin],
        ["customer-portal"] = [UserRole.Customer]
    };

    public static bool Allows(string? clientId, UserRole role) =>
        !string.IsNullOrWhiteSpace(clientId)
        && Allowed.TryGetValue(clientId, out var roles)
        && roles.Contains(role);
}
