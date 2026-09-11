using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Entities;

public class LoginEligibilityTests
{
    [Test]
    public void ActiveSystemAdmin_WithoutTenant_MayComplete()
    {
        var user = User.Create(
            UserRole.SystemAdmin,
            name: "Ada",
            email: "ada@localhost",
            identityUserId: "id-1");

        LoginEligibility.CanComplete(user, tenant: null).ShouldBeTrue();
    }

    [Test]
    public void ActiveSystemAdmin_WithTenant_IsRejected()
    {
        var user = User.Create(
            UserRole.SystemAdmin,
            name: "Ada",
            email: "ada@localhost",
            identityUserId: "id-1");

        LoginEligibility.CanComplete(user, EnabledTenant(10)).ShouldBeFalse();
    }

    [Test]
    public void ActiveTenantUser_WithEnabledMatchingTenant_MayComplete()
    {
        var tenant = EnabledTenant(10);
        var user = User.Create(
            UserRole.TenantAdmin,
            name: "Bea",
            email: "bea@firm",
            identityUserId: "id-2",
            tenantId: 10);

        LoginEligibility.CanComplete(user, tenant).ShouldBeTrue();
    }

    [Test]
    public void ActiveTenantUser_WithDisabledTenant_IsRejected()
    {
        var tenant = EnabledTenant(10);
        tenant.Disable();
        var user = User.Create(
            UserRole.TenantAdmin,
            name: "Bea",
            email: "bea@firm",
            identityUserId: "id-2",
            tenantId: 10);

        LoginEligibility.CanComplete(user, tenant).ShouldBeFalse();
    }

    [Test]
    public void ActiveTenantUser_WithoutTenant_IsRejected()
    {
        var user = User.Create(
            UserRole.TenantAdmin,
            name: "Bea",
            email: "bea@firm",
            identityUserId: "id-2",
            tenantId: 10);

        LoginEligibility.CanComplete(user, tenant: null).ShouldBeFalse();
    }

    [Test]
    public void ActiveTenantUser_WithDifferentTenant_IsRejected()
    {
        var other = EnabledTenant(99);
        var user = User.Create(
            UserRole.TenantAdmin,
            name: "Bea",
            email: "bea@firm",
            identityUserId: "id-2",
            tenantId: 10);

        LoginEligibility.CanComplete(user, other).ShouldBeFalse();
    }

    [Test]
    public void PendingActivation_IsRejected()
    {
        var tenant = EnabledTenant(10);
        var user = User.Create(
            UserRole.TenantAdmin,
            name: "Bea",
            email: "bea@firm",
            identityUserId: "id-2",
            tenantId: 10,
            withPassword: false);

        LoginEligibility.CanComplete(user, tenant).ShouldBeFalse();
    }

    [Test]
    public void DisabledUser_IsRejected()
    {
        var tenant = EnabledTenant(10);
        var user = User.Create(
            UserRole.TenantAdmin,
            name: "Bea",
            email: "bea@firm",
            identityUserId: "id-2",
            tenantId: 10);
        user.Disable();

        LoginEligibility.CanComplete(user, tenant).ShouldBeFalse();
    }

    private static Tenant EnabledTenant(int id)
    {
        var tenant = Tenant.Create("Acme", "acme");
        tenant.Id = id;
        return tenant;
    }
}
