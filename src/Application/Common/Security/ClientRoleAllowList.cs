using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.Common.Security;

public static class ClientRoleAllowList
{
    // Reserved, do not register the clients: customer-portal → Customer;
    // Back Office (name unlocked) → SystemAdmin.

    private static readonly Dictionary<string, HashSet<UserRole>> Allowed = new(StringComparer.Ordinal)
    {
        ["adviser-portal"] = [UserRole.SystemAdmin, UserRole.TenantAdmin, UserRole.Adviser]
    };

    public static bool Allows(string? clientId, UserRole role) =>
        !string.IsNullOrWhiteSpace(clientId)
        && Allowed.TryGetValue(clientId, out var roles)
        && roles.Contains(role);
}
