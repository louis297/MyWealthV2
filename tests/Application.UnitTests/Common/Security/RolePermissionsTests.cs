using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Enums;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Application.UnitTests.Common.Security;

public class RolePermissionsTests
{
    [TestCase(UserRole.SystemAdmin, Policies.TenantsManage, true)]
    [TestCase(UserRole.SystemAdmin, Policies.TenantAdminsManage, true)]
    [TestCase(UserRole.SystemAdmin, Policies.AdvisersManage, false)]
    [TestCase(UserRole.SystemAdmin, Policies.CustomersManage, false)]
    [TestCase(UserRole.SystemAdmin, Policies.CustomersManageOwn, false)]
    [TestCase(UserRole.SystemAdmin, Policies.UsersMe, true)]
    [TestCase(UserRole.SystemAdmin, Policies.TenantsRead, true)]
    [TestCase(UserRole.TenantAdmin, Policies.TenantsManage, false)]
    [TestCase(UserRole.TenantAdmin, Policies.TenantAdminsManage, false)]
    [TestCase(UserRole.TenantAdmin, Policies.AdvisersManage, true)]
    [TestCase(UserRole.TenantAdmin, Policies.CustomersManage, true)]
    [TestCase(UserRole.TenantAdmin, Policies.CustomersManageOwn, false)]
    [TestCase(UserRole.TenantAdmin, Policies.UsersMe, true)]
    [TestCase(UserRole.TenantAdmin, Policies.TenantsRead, true)]
    [TestCase(UserRole.Adviser, Policies.TenantsManage, false)]
    [TestCase(UserRole.Adviser, Policies.TenantAdminsManage, false)]
    [TestCase(UserRole.Adviser, Policies.AdvisersManage, false)]
    [TestCase(UserRole.Adviser, Policies.CustomersManage, false)]
    [TestCase(UserRole.Adviser, Policies.CustomersManageOwn, true)]
    [TestCase(UserRole.Adviser, Policies.UsersMe, true)]
    [TestCase(UserRole.Adviser, Policies.TenantsRead, true)]
    [TestCase(UserRole.Customer, Policies.TenantsManage, false)]
    [TestCase(UserRole.Customer, Policies.TenantAdminsManage, false)]
    [TestCase(UserRole.Customer, Policies.AdvisersManage, false)]
    [TestCase(UserRole.Customer, Policies.CustomersManage, false)]
    [TestCase(UserRole.Customer, Policies.CustomersManageOwn, false)]
    [TestCase(UserRole.Customer, Policies.UsersMe, true)]
    [TestCase(UserRole.Customer, Policies.TenantsRead, false)]
    public void Has_MatchesPhase1Map(UserRole role, string policy, bool expected)
    {
        RolePermissions.Has(role, policy).ShouldBe(expected);
    }

    [TestCase(UserRole.SystemAdmin, Policies.InstrumentsRead, true)]
    [TestCase(UserRole.SystemAdmin, Policies.InstrumentsCreate, true)]
    [TestCase(UserRole.SystemAdmin, Policies.InstrumentsManage, true)]
    [TestCase(UserRole.TenantAdmin, Policies.InstrumentsRead, true)]
    [TestCase(UserRole.TenantAdmin, Policies.InstrumentsCreate, true)]
    [TestCase(UserRole.TenantAdmin, Policies.InstrumentsManage, true)]
    [TestCase(UserRole.Adviser, Policies.InstrumentsRead, true)]
    [TestCase(UserRole.Adviser, Policies.InstrumentsCreate, true)]
    [TestCase(UserRole.Adviser, Policies.InstrumentsManage, false)]
    [TestCase(UserRole.Customer, Policies.InstrumentsRead, false)]
    [TestCase(UserRole.Customer, Policies.InstrumentsCreate, false)]
    [TestCase(UserRole.Customer, Policies.InstrumentsManage, false)]
    public void Has_MatchesInstrumentsMap(UserRole role, string policy, bool expected)
    {
        RolePermissions.Has(role, policy).ShouldBe(expected);
    }

    [TestCase(UserRole.SystemAdmin, Policies.AccountsRead, true)]
    [TestCase(UserRole.SystemAdmin, Policies.AccountsCreate, true)]
    [TestCase(UserRole.SystemAdmin, Policies.AccountsManage, true)]
    [TestCase(UserRole.TenantAdmin, Policies.AccountsRead, true)]
    [TestCase(UserRole.TenantAdmin, Policies.AccountsCreate, true)]
    [TestCase(UserRole.TenantAdmin, Policies.AccountsManage, true)]
    [TestCase(UserRole.Adviser, Policies.AccountsRead, true)]
    [TestCase(UserRole.Adviser, Policies.AccountsCreate, true)]
    [TestCase(UserRole.Adviser, Policies.AccountsManage, true)]
    [TestCase(UserRole.Customer, Policies.AccountsRead, false)]
    [TestCase(UserRole.Customer, Policies.AccountsCreate, false)]
    [TestCase(UserRole.Customer, Policies.AccountsManage, false)]
    public void Has_MatchesAccountsMap(UserRole role, string policy, bool expected)
    {
        RolePermissions.Has(role, policy).ShouldBe(expected);
    }

    [TestCase(UserRole.SystemAdmin, Policies.TransactionsRead, true)]
    [TestCase(UserRole.SystemAdmin, Policies.TransactionsCreate, true)]
    [TestCase(UserRole.TenantAdmin, Policies.TransactionsRead, true)]
    [TestCase(UserRole.TenantAdmin, Policies.TransactionsCreate, true)]
    [TestCase(UserRole.Adviser, Policies.TransactionsRead, true)]
    [TestCase(UserRole.Adviser, Policies.TransactionsCreate, true)]
    [TestCase(UserRole.Customer, Policies.TransactionsRead, false)]
    [TestCase(UserRole.Customer, Policies.TransactionsCreate, false)]
    public void Has_MatchesTransactionsMap(UserRole role, string policy, bool expected)
    {
        RolePermissions.Has(role, policy).ShouldBe(expected);
    }
}
