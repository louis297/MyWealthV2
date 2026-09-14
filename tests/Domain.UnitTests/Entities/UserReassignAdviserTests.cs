using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Events;
using MyWealthV2.Domain.Exceptions;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Entities;

public class UserReassignAdviserTests
{
    [Test]
    public void CreateCustomerWithPassword_SetsShapeAndDoesNotRaiseReassigned()
    {
        var customer = User.Create(
            UserRole.Customer,
            name: "Jordan Lee",
            email: "jordan@north.example",
            identityUserId: "id-cu",
            tenantId: 10,
            adviserId: 20,
            withPassword: true);

        customer.TenantId.ShouldBe(10);
        customer.AdviserId.ShouldBe(20);
        customer.Status.ShouldBe(UserStatus.Active);
        customer.DomainEvents.OfType<UserCreated>().ShouldHaveSingleItem();
        customer.DomainEvents.OfType<UserActivated>().ShouldHaveSingleItem();
        customer.DomainEvents.OfType<CustomerAdviserReassigned>().ShouldBeEmpty();
    }

    [Test]
    public void ReassignAdviser_WritesNewIdAndRaisesEventOnce()
    {
        var customer = ActiveCustomer();
        customer.Id = 42;

        customer.ReassignAdviser(30);

        customer.AdviserId.ShouldBe(30);
        var reassigned = customer.DomainEvents.OfType<CustomerAdviserReassigned>().ShouldHaveSingleItem();
        reassigned.CustomerId.ShouldBe(42);
        reassigned.TenantId.ShouldBe(10);
        reassigned.PreviousAdviserId.ShouldBe(20);
        reassigned.NewAdviserId.ShouldBe(30);
    }

    [Test]
    public void ReassignAdviser_SameId_IsNoOp()
    {
        var customer = ActiveCustomer();

        customer.ReassignAdviser(20);

        customer.AdviserId.ShouldBe(20);
        customer.DomainEvents.OfType<CustomerAdviserReassigned>().ShouldBeEmpty();
    }

    [Test]
    public void ReassignAdviser_OnNonCustomer_Throws()
    {
        var adviser = User.Create(
            UserRole.Adviser,
            name: "Sam Reed",
            email: "sam@north.example",
            identityUserId: "id-ad",
            tenantId: 10);

        Should.Throw<DomainException>(() => adviser.ReassignAdviser(30));
        adviser.AdviserId.ShouldBeNull();
        adviser.DomainEvents.OfType<CustomerAdviserReassigned>().ShouldBeEmpty();
    }

    [Test]
    public void ReassignAdviser_EmptyNewId_Throws()
    {
        var customer = ActiveCustomer();

        Should.Throw<DomainException>(() => customer.ReassignAdviser(0));
        customer.AdviserId.ShouldBe(20);
        customer.DomainEvents.OfType<CustomerAdviserReassigned>().ShouldBeEmpty();
    }

    [Test]
    public void Disable_OnCustomer_MatchesTenantAdmin()
    {
        var customer = ActiveCustomer();

        customer.Disable();

        customer.Status.ShouldBe(UserStatus.Disabled);
        customer.DomainEvents.OfType<UserDisabled>().ShouldHaveSingleItem();
    }

    private static User ActiveCustomer() =>
        User.Create(
            UserRole.Customer,
            name: "Jordan Lee",
            email: "jordan@north.example",
            identityUserId: "id-cu",
            tenantId: 10,
            adviserId: 20,
            withPassword: true);
}
