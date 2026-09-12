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
    [TestCase(UserRole.TenantAdmin, Policies.TenantsManage, false)]
    [TestCase(UserRole.TenantAdmin, Policies.TenantAdminsManage, false)]
    [TestCase(UserRole.TenantAdmin, Policies.AdvisersManage, true)]
    [TestCase(UserRole.TenantAdmin, Policies.CustomersManage, true)]
    [TestCase(UserRole.TenantAdmin, Policies.CustomersManageOwn, false)]
    [TestCase(UserRole.TenantAdmin, Policies.UsersMe, true)]
    [TestCase(UserRole.Adviser, Policies.TenantsManage, false)]
    [TestCase(UserRole.Adviser, Policies.TenantAdminsManage, false)]
    [TestCase(UserRole.Adviser, Policies.AdvisersManage, false)]
    [TestCase(UserRole.Adviser, Policies.CustomersManage, false)]
    [TestCase(UserRole.Adviser, Policies.CustomersManageOwn, true)]
    [TestCase(UserRole.Adviser, Policies.UsersMe, true)]
    [TestCase(UserRole.Customer, Policies.TenantsManage, false)]
    [TestCase(UserRole.Customer, Policies.TenantAdminsManage, false)]
    [TestCase(UserRole.Customer, Policies.AdvisersManage, false)]
    [TestCase(UserRole.Customer, Policies.CustomersManage, false)]
    [TestCase(UserRole.Customer, Policies.CustomersManageOwn, false)]
    [TestCase(UserRole.Customer, Policies.UsersMe, true)]
    public void Has_MatchesPhase1Map(UserRole role, string policy, bool expected)
    {
        RolePermissions.Has(role, policy).ShouldBe(expected);
    }
}
