using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Events;
using MyWealthV2.Domain.Exceptions;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Entities;

public class TenantTests
{
    [Test]
    public void Create_RaisesTenantCreated()
    {
        var tenant = Tenant.Create("Acme", "acme", Nzd());

        tenant.DomainEvents.OfType<TenantCreated>().ShouldHaveSingleItem();
        tenant.IsActive.ShouldBeTrue();
    }

    [Test]
    public void Rename_RejectsBlank()
    {
        var tenant = Tenant.Create("Acme", "acme", Nzd());

        Should.Throw<DomainException>(() => tenant.Rename(""));
        Should.Throw<DomainException>(() => tenant.Rename(" "));
        tenant.Name.ShouldBe("Acme");
    }

    [Test]
    public void Rename_SetsName()
    {
        var tenant = Tenant.Create("Acme", "acme", Nzd());

        tenant.Rename("Acme Advisory");

        tenant.Name.ShouldBe("Acme Advisory");
        tenant.Code.ShouldBe("acme");
    }

    [Test]
    public void Enable_FlipsAndRaisesTenantEnabledOnce()
    {
        var tenant = Tenant.Create("Acme", "acme", Nzd());
        tenant.Disable();
        tenant.ClearDomainEvents();

        tenant.Enable();

        tenant.IsActive.ShouldBeTrue();
        tenant.DomainEvents.OfType<TenantEnabled>().ShouldHaveSingleItem();
    }

    [Test]
    public void Enable_WhenAlreadyEnabled_IsNoOp()
    {
        var tenant = Tenant.Create("Acme", "acme", Nzd());
        tenant.ClearDomainEvents();

        tenant.Enable();

        tenant.IsActive.ShouldBeTrue();
        tenant.DomainEvents.ShouldBeEmpty();
    }

    [Test]
    public void Disable_WhenAlreadyDisabled_IsNoOp()
    {
        var tenant = Tenant.Create("Acme", "acme", Nzd());
        tenant.Disable();
        tenant.ClearDomainEvents();

        tenant.Disable();

        tenant.IsActive.ShouldBeFalse();
        tenant.DomainEvents.ShouldBeEmpty();
    }

    [Test]
    public void Disable_RaisesTenantDisabled()
    {
        var tenant = Tenant.Create("Acme", "acme", Nzd());

        tenant.Disable();

        tenant.IsActive.ShouldBeFalse();
        tenant.DomainEvents.OfType<TenantDisabled>().ShouldHaveSingleItem();
    }

    [Test]
    public void Create_RejectsEmptyReportingCurrency()
    {
        Should.Throw<DomainException>(() => Tenant.Create("Acme", "acme", reportingCurrency: null!));
    }

    [Test]
    public void Create_RejectsDisabledReportingCurrency()
    {
        var disabled = Currency.Create("JPY", "Japanese Yen", 0, isEnabled: false);

        Should.Throw<DomainException>(() => Tenant.Create("Acme", "acme", disabled));
    }

    [Test]
    public void Create_SetsEnabledReportingCurrency()
    {
        var tenant = Tenant.Create("Acme", "acme", Nzd());

        tenant.ReportingCurrency.ShouldBe("NZD");
    }

    [Test]
    public void SetReportingCurrency_RejectsDisabledCode()
    {
        var tenant = Tenant.Create("Acme", "acme", Nzd());
        var disabled = Currency.Create("JPY", "Japanese Yen", 0, isEnabled: false);

        Should.Throw<DomainException>(() => tenant.SetReportingCurrency(disabled));
        tenant.ReportingCurrency.ShouldBe("NZD");
    }

    [Test]
    public void SetReportingCurrency_AcceptsEnabledCode()
    {
        var tenant = Tenant.Create("Acme", "acme", Nzd());

        tenant.SetReportingCurrency(Currency.Create("AUD", "Australian Dollar", 2));

        tenant.ReportingCurrency.ShouldBe("AUD");
    }

    private static Currency Nzd() => Currency.Create("NZD", "New Zealand Dollar", 2);
}

