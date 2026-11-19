using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Entities;

/// <summary>Simulator spec R1, R4: the platform-level role <c>SimulatorOperator</c>.</summary>
public class SimulatorOperatorTests
{
    [Test]
    public void Create_HasNoTenantOrAdviser()
    {
        var user = Operator();

        user.Role.ShouldBe(UserRole.SimulatorOperator);
        user.TenantId.ShouldBeNull();
        user.AdviserId.ShouldBeNull();
        user.Status.ShouldBe(UserStatus.Active);
    }

    [Test]
    public void Create_WithTenant_IsRejected()
    {
        Should.Throw<DomainException>(() => User.Create(
            UserRole.SimulatorOperator,
            name: "Sim",
            email: "sim@localhost",
            identityUserId: "id-sim",
            tenantId: 1));
    }

    [Test]
    public void Create_WithAdviser_IsRejected()
    {
        Should.Throw<DomainException>(() => User.Create(
            UserRole.SimulatorOperator,
            name: "Sim",
            email: "sim@localhost",
            identityUserId: "id-sim",
            adviserId: 2));
    }

    [Test]
    public void ActiveOperator_WithoutTenant_MayCompleteLogin()
    {
        LoginEligibility.CanComplete(Operator(), tenant: null).ShouldBeTrue();
    }

    [Test]
    public void ActiveOperator_WithTenant_IsRejected()
    {
        var tenant = Tenant.Create("Acme", "acme", Currency.Create("NZD", "New Zealand Dollar", 2));
        tenant.Id = 10;

        LoginEligibility.CanComplete(Operator(), tenant).ShouldBeFalse();
    }

    [Test]
    public void DisabledOperator_IsRejected()
    {
        var user = Operator();
        user.Disable();

        LoginEligibility.CanComplete(user, tenant: null).ShouldBeFalse();
    }

    private static User Operator() => User.Create(
        UserRole.SimulatorOperator,
        name: "Sim",
        email: "sim@localhost",
        identityUserId: "id-sim");
}
