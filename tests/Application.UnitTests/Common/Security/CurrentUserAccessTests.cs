using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Application.UnitTests.Common.Security;

public class CurrentUserAccessTests
{
    [Test]
    public void SystemAdmin_WithEmptyTenantClaims_Resolves()
    {
        var user = User.Create(
            UserRole.SystemAdmin,
            name: "Ada",
            email: "ada@localhost",
            identityUserId: "id-1");

        var resolved = CurrentUserAccess.Resolve(
            user,
            tenant: null,
            tenantIdClaim: null,
            tenantCodeClaim: null);

        resolved.ShouldNotBeNull();
        resolved.User.ShouldBe(user);
        resolved.Tenant.ShouldBeNull();
    }

    [Test]
    public void SystemAdmin_WithTenantClaim_IsRejected()
    {
        var user = User.Create(
            UserRole.SystemAdmin,
            name: "Ada",
            email: "ada@localhost",
            identityUserId: "id-1");

        CurrentUserAccess.Resolve(
            user,
            tenant: null,
            tenantIdClaim: Guid.NewGuid().ToString(),
            tenantCodeClaim: "acme").ShouldBeNull();
    }

    [Test]
    public void TenantUser_WithMatchingClaims_Resolves()
    {
        var tenant = Tenant.Create("Acme", "acme");
        tenant.Id = 10;
        var user = User.Create(
            UserRole.TenantAdmin,
            name: "Bea",
            email: "bea@acme",
            identityUserId: "id-2",
            tenantId: 10);

        var resolved = CurrentUserAccess.Resolve(
            user,
            tenant,
            tenantIdClaim: tenant.PublicId.ToString(),
            tenantCodeClaim: "acme");

        resolved.ShouldNotBeNull();
        resolved.Tenant.ShouldBe(tenant);
    }

    [Test]
    public void TenantUser_WithMismatchedTenantIdClaim_IsRejected()
    {
        var tenant = Tenant.Create("Acme", "acme");
        tenant.Id = 10;
        var user = User.Create(
            UserRole.TenantAdmin,
            name: "Bea",
            email: "bea@acme",
            identityUserId: "id-2",
            tenantId: 10);

        CurrentUserAccess.Resolve(
            user,
            tenant,
            tenantIdClaim: Guid.NewGuid().ToString(),
            tenantCodeClaim: "acme").ShouldBeNull();
    }

    [Test]
    public void TenantUser_WithMismatchedTenantCodeClaim_IsRejected()
    {
        var tenant = Tenant.Create("Acme", "acme");
        tenant.Id = 10;
        var user = User.Create(
            UserRole.TenantAdmin,
            name: "Bea",
            email: "bea@acme",
            identityUserId: "id-2",
            tenantId: 10);

        CurrentUserAccess.Resolve(
            user,
            tenant,
            tenantIdClaim: tenant.PublicId.ToString(),
            tenantCodeClaim: "other").ShouldBeNull();
    }

    [Test]
    public void MissingUser_IsRejected()
    {
        CurrentUserAccess.Resolve(
            user: null,
            tenant: null,
            tenantIdClaim: null,
            tenantCodeClaim: null).ShouldBeNull();
    }

    [Test]
    public void TenantUser_WithoutTenantRow_IsRejected()
    {
        var user = User.Create(
            UserRole.TenantAdmin,
            name: "Bea",
            email: "bea@acme",
            identityUserId: "id-2",
            tenantId: 10);

        CurrentUserAccess.Resolve(
            user,
            tenant: null,
            tenantIdClaim: Guid.NewGuid().ToString(),
            tenantCodeClaim: "acme").ShouldBeNull();
    }
}
