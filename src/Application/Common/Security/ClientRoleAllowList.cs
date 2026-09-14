using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.Common.Security;

public static class ClientRoleAllowList
{
    public static bool Allows(string? clientId, UserRole role) => false;
}
