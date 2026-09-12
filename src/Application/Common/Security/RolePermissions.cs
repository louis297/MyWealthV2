using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.Common.Security;

public static class RolePermissions
{
    private static readonly Dictionary<UserRole, HashSet<string>> Map = new()
    {
        [UserRole.SystemAdmin] = [Policies.TenantsManage, Policies.TenantAdminsManage, Policies.UsersMe],
        [UserRole.TenantAdmin] = [Policies.AdvisersManage, Policies.CustomersManage, Policies.UsersMe],
        [UserRole.Adviser] = [Policies.CustomersManageOwn, Policies.UsersMe],
        [UserRole.Customer] = [Policies.UsersMe]
    };

    public static bool Has(UserRole role, string policy) =>
        Map.TryGetValue(role, out var policies) && policies.Contains(policy);
}
