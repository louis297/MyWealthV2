namespace MyWealthV2.Application.Common.Security;

public static class Policies
{
    public const string TenantsManage = "tenants.manage";
    public const string TenantsRead = "tenants.read";
    public const string TenantAdminsManage = "tenant-admins.manage";
    public const string AdvisersManage = "advisers.manage";
    public const string CustomersManage = "customers.manage";
    public const string CustomersManageOwn = "customers.manage-own";
    public const string UsersMe = "users.me";
}
