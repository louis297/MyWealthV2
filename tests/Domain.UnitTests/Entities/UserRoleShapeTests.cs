using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Entities;

public class UserRoleShapeTests
{
    [Test]
    public void SystemAdmin_HasNoTenantOrAdviser()
    {
        var user = User.Create(
            UserRole.SystemAdmin,
            name: "Ada",
            email: "ada@localhost",
            identityUserId: "id-1");

        user.TenantId.ShouldBeNull();
        user.AdviserId.ShouldBeNull();
        user.Role.ShouldBe(UserRole.SystemAdmin);
    }

    [Test]
    public void SystemAdmin_WithTenant_IsRejected()
    {
        Should.Throw<DomainException>(() => User.Create(
            UserRole.SystemAdmin,
            name: "Ada",
            email: "ada@localhost",
            identityUserId: "id-1",
            tenantId: 1));
    }

    [Test]
    public void TenantAdmin_RequiresTenantAndNoAdviser()
    {
        var user = User.Create(
            UserRole.TenantAdmin,
            name: "Bea",
            email: "bea@firm",
            identityUserId: "id-2",
            tenantId: 10);

        user.TenantId.ShouldBe(10);
        user.AdviserId.ShouldBeNull();
    }

    [Test]
    public void TenantAdmin_WithoutTenant_IsRejected()
    {
        Should.Throw<DomainException>(() => User.Create(
            UserRole.TenantAdmin,
            name: "Bea",
            email: "bea@firm",
            identityUserId: "id-2"));
    }

    [Test]
    public void Adviser_WithAdviserId_IsRejected()
    {
        Should.Throw<DomainException>(() => User.Create(
            UserRole.Adviser,
            name: "Cara",
            email: "cara@firm",
            identityUserId: "id-3",
            tenantId: 10,
            adviserId: 99));
    }

    [Test]
    public void Customer_RequiresTenantAndAdviser()
    {
        var user = User.Create(
            UserRole.Customer,
            name: "Dee",
            email: "dee@firm",
            identityUserId: "id-4",
            tenantId: 10,
            adviserId: 20);

        user.TenantId.ShouldBe(10);
        user.AdviserId.ShouldBe(20);
    }

    [Test]
    public void Customer_WithoutAdviser_IsRejected()
    {
        Should.Throw<DomainException>(() => User.Create(
            UserRole.Customer,
            name: "Dee",
            email: "dee@firm",
            identityUserId: "id-4",
            tenantId: 10));
    }

    [Test]
    public void MissingIdentityUserId_IsRejected()
    {
        Should.Throw<DomainException>(() => User.Create(
            UserRole.SystemAdmin,
            name: "Ada",
            email: "ada@localhost",
            identityUserId: " "));
    }
}
