using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.Common.Security;

public static class RolePermissions
{
    public static bool Has(UserRole role, string policy) => false;
}
