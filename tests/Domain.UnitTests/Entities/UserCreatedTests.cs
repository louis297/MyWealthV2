using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Events;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Entities;

public class UserCreatedTests
{
    [Test]
    public void CreateTenantAdminWithPassword_RaisesUserCreatedOnceAndLandsActive()
    {
        var user = User.Create(
            UserRole.TenantAdmin,
            name: "Alex Chen",
            email: "Alex@north.example",
            identityUserId: "id-ta",
            tenantId: 10,
            withPassword: true);

        user.TenantId.ShouldBe(10);
        user.AdviserId.ShouldBeNull();
        user.Status.ShouldBe(UserStatus.Active);

        var created = user.DomainEvents.OfType<UserCreated>().ShouldHaveSingleItem();
        created.User.ShouldBe(user);
        user.DomainEvents.OfType<UserActivated>().ShouldHaveSingleItem();
    }

    [Test]
    public void CreateWithoutPassword_StillRaisesUserCreatedOnce()
    {
        var user = User.Create(
            UserRole.TenantAdmin,
            name: "Alex Chen",
            email: "alex@north.example",
            identityUserId: "id-ta",
            tenantId: 10,
            withPassword: false);

        user.Status.ShouldBe(UserStatus.PendingActivation);
        var created = user.DomainEvents.OfType<UserCreated>().ShouldHaveSingleItem();
        created.User.ShouldBe(user);
        user.DomainEvents.OfType<UserActivated>().ShouldBeEmpty();
    }
}
