using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Events;
using MyWealthV2.Domain.Exceptions;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Entities;

public class UserStatusTests
{
    [Test]
    public void CreateWithPassword_LandsInActive()
    {
        var user = User.Create(
            UserRole.SystemAdmin,
            name: "Ada",
            email: "ada@localhost",
            identityUserId: "id-1",
            withPassword: true);

        user.Status.ShouldBe(UserStatus.Active);
        user.DomainEvents.OfType<UserActivated>().ShouldHaveSingleItem();
    }

    [Test]
    public void CreateWithoutPassword_LandsInPendingActivation()
    {
        var user = User.Create(
            UserRole.SystemAdmin,
            name: "Ada",
            email: "ada@localhost",
            identityUserId: "id-1",
            withPassword: false);

        user.Status.ShouldBe(UserStatus.PendingActivation);
        user.DomainEvents.OfType<UserActivated>().ShouldBeEmpty();
    }

    [Test]
    public void Disable_FromActive_BecomesDisabled()
    {
        var user = ActiveSystemAdmin();

        user.Disable();

        user.Status.ShouldBe(UserStatus.Disabled);
        user.DomainEvents.OfType<UserDisabled>().ShouldHaveSingleItem();
    }

    [Test]
    public void Disable_FromPendingActivation_BecomesDisabled()
    {
        var user = User.Create(
            UserRole.SystemAdmin,
            name: "Ada",
            email: "ada@localhost",
            identityUserId: "id-1",
            withPassword: false);

        user.Disable();

        user.Status.ShouldBe(UserStatus.Disabled);
        user.DomainEvents.OfType<UserDisabled>().ShouldHaveSingleItem();
    }

    [Test]
    public void Enable_FromDisabled_BecomesActive()
    {
        var user = ActiveSystemAdmin();
        user.Disable();
        user.ClearDomainEvents();

        user.Enable();

        user.Status.ShouldBe(UserStatus.Active);
        user.DomainEvents.OfType<UserActivated>().ShouldHaveSingleItem();
    }

    [Test]
    public void Activate_FromPendingActivation_BecomesActive()
    {
        var user = User.Create(
            UserRole.SystemAdmin,
            name: "Ada",
            email: "ada@localhost",
            identityUserId: "id-1",
            withPassword: false);

        user.Activate();

        user.Status.ShouldBe(UserStatus.Active);
        user.DomainEvents.OfType<UserActivated>().ShouldHaveSingleItem();
    }

    [Test]
    public void Disable_FromDisabled_IsRejected()
    {
        var user = ActiveSystemAdmin();
        user.Disable();

        Should.Throw<DomainException>(() => user.Disable());
    }

    [Test]
    public void Enable_FromActive_IsRejected()
    {
        var user = ActiveSystemAdmin();

        Should.Throw<DomainException>(() => user.Enable());
    }

    [Test]
    public void Enable_FromPendingActivation_IsRejected()
    {
        var user = User.Create(
            UserRole.SystemAdmin,
            name: "Ada",
            email: "ada@localhost",
            identityUserId: "id-1",
            withPassword: false);

        Should.Throw<DomainException>(() => user.Enable());
    }

    [Test]
    public void Activate_FromActive_IsRejected()
    {
        var user = ActiveSystemAdmin();

        Should.Throw<DomainException>(() => user.Activate());
    }

    private static User ActiveSystemAdmin() =>
        User.Create(
            UserRole.SystemAdmin,
            name: "Ada",
            email: "ada@localhost",
            identityUserId: "id-1",
            withPassword: true);
}
