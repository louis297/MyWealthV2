using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Events;
using MyWealthV2.Domain.Exceptions;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Entities;

public class UserDisableAdviserTests
{
    [Test]
    public void DisableAdviser_False_OnActiveAdviser_DisablesAndRaisesUserDisabledOnce()
    {
        var adviser = ActiveAdviser();

        adviser.DisableAdviser(hasNonDisabledAssignedCustomers: false);

        adviser.Status.ShouldBe(UserStatus.Disabled);
        adviser.DomainEvents.OfType<UserDisabled>().ShouldHaveSingleItem();
    }

    [Test]
    public void DisableAdviser_True_Throws_StatusUnchanged_NoEvent()
    {
        var adviser = ActiveAdviser();

        Should.Throw<DomainException>(() => adviser.DisableAdviser(hasNonDisabledAssignedCustomers: true))
            .Message.ShouldBe("Reassign or disable assigned customers before disabling this adviser.");

        adviser.Status.ShouldBe(UserStatus.Active);
        adviser.DomainEvents.OfType<UserDisabled>().ShouldBeEmpty();
    }

    [Test]
    public void DisableAdviser_OnTenantAdmin_Throws()
    {
        var tenantAdmin = User.Create(
            UserRole.TenantAdmin,
            name: "Bea",
            email: "bea@firm",
            identityUserId: "id-ta",
            tenantId: 10);

        Should.Throw<DomainException>(() => tenantAdmin.DisableAdviser(false));
        tenantAdmin.Status.ShouldBe(UserStatus.Active);
        tenantAdmin.DomainEvents.OfType<UserDisabled>().ShouldBeEmpty();
    }

    [Test]
    public void DisableAdviser_OnCustomer_Throws()
    {
        var customer = User.Create(
            UserRole.Customer,
            name: "Dee",
            email: "dee@firm",
            identityUserId: "id-cu",
            tenantId: 10,
            adviserId: 20);

        Should.Throw<DomainException>(() => customer.DisableAdviser(false));
        customer.Status.ShouldBe(UserStatus.Active);
    }

    [Test]
    public void DisableAdviser_OnSystemAdmin_Throws()
    {
        var admin = User.Create(
            UserRole.SystemAdmin,
            name: "Ada",
            email: "ada@localhost",
            identityUserId: "id-sa");

        Should.Throw<DomainException>(() => admin.DisableAdviser(false));
        admin.Status.ShouldBe(UserStatus.Active);
    }

    [Test]
    public void Disable_OnTenantAdmin_StillDisables()
    {
        var tenantAdmin = User.Create(
            UserRole.TenantAdmin,
            name: "Bea",
            email: "bea@firm",
            identityUserId: "id-ta",
            tenantId: 10);

        tenantAdmin.Disable();

        tenantAdmin.Status.ShouldBe(UserStatus.Disabled);
        tenantAdmin.DomainEvents.OfType<UserDisabled>().ShouldHaveSingleItem();
    }

    private static User ActiveAdviser() =>
        User.Create(
            UserRole.Adviser,
            name: "Sam Reed",
            email: "sam@north.example",
            identityUserId: "id-ad",
            tenantId: 10,
            withPassword: true);
}
