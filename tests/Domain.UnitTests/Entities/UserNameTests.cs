using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Events;
using MyWealthV2.Domain.Exceptions;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Entities;

public class UserNameTests
{
    [Test]
    public void ChangeName_UpdatesName()
    {
        var user = User.Create(
            UserRole.SystemAdmin,
            name: "Ada",
            email: "ada@localhost",
            identityUserId: "id-1");

        user.ChangeName("Ada Lovelace");

        user.Name.ShouldBe("Ada Lovelace");
    }

    [Test]
    public void ChangeName_Blank_IsRejected()
    {
        var user = User.Create(
            UserRole.SystemAdmin,
            name: "Ada",
            email: "ada@localhost",
            identityUserId: "id-1");

        Should.Throw<DomainException>(() => user.ChangeName(" "));
    }

    [Test]
    public void RecordPasswordChanged_RaisesEvent()
    {
        var user = User.Create(
            UserRole.SystemAdmin,
            name: "Ada",
            email: "ada@localhost",
            identityUserId: "id-1");
        user.ClearDomainEvents();

        user.RecordPasswordChanged();

        user.DomainEvents.OfType<UserPasswordChanged>().ShouldHaveSingleItem();
    }
}
