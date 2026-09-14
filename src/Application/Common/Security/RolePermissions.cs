using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.Common.Security;

public static class RolePermissions
{
    private static readonly Dictionary<UserRole, HashSet<string>> Map = new()
    {
        [UserRole.SystemAdmin] = [Policies.TenantsManage, Policies.TenantsRead, Policies.TenantAdminsManage, Policies.UsersMe],
        [UserRole.TenantAdmin] = [Policies.TenantsRead, Policies.AdvisersManage, Policies.CustomersManage, Policies.UsersMe],
        [UserRole.Adviser] = [Policies.TenantsRead, Policies.CustomersManageOwn, Policies.UsersMe],
        [UserRole.Customer] = [Policies.UsersMe]
    };

    public static bool Has(UserRole role, string policy) =>
        Map.TryGetValue(role, out var policies) && policies.Contains(policy);
}
