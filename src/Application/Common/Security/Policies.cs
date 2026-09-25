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
    public const string InstrumentsRead = "instruments.read";
    public const string InstrumentsCreate = "instruments.create";
    public const string InstrumentsManage = "instruments.manage";
    public const string AccountsRead = "accounts.read";
    public const string AccountsCreate = "accounts.create";
    public const string AccountsManage = "accounts.manage";
    public const string TransactionsRead = "transactions.read";
    public const string TransactionsCreate = "transactions.create";
}
