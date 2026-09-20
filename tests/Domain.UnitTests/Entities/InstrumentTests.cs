using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Events;
using MyWealthV2.Domain.Exceptions;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Entities;

public class InstrumentTests
{
    [Test]
    public void Create_NormalisesSymbolToUpperCase()
    {
        var instrument = Instrument.Create(1, " vti ", "Vanguard", Usd());

        instrument.Symbol.ShouldBe("VTI");
    }

    [Test]
    public void Create_RaisesInstrumentCreated()
    {
        var instrument = Instrument.Create(1, "VTI", "Vanguard", Usd());

        instrument.DomainEvents.OfType<InstrumentCreated>().ShouldHaveSingleItem();
        instrument.IsEnabled.ShouldBeTrue();
        instrument.TenantId.ShouldBe(1);
        instrument.Name.ShouldBe("Vanguard");
        instrument.QuoteCurrency.ShouldBe("USD");
    }

    [Test]
    public void Create_TrimsNameAndPreservesCasing()
    {
        var instrument = Instrument.Create(1, "VTI", "  Vanguard Total  ", Usd());

        instrument.Name.ShouldBe("Vanguard Total");
    }

    [Test]
    public void Create_AllowsDotAndDashInSymbol()
    {
        var instrument = Instrument.Create(1, "nzd-cash", "Cash", Nzd());

        instrument.Symbol.ShouldBe("NZD-CASH");
    }

    [Test]
    public void QuoteCurrency_CannotChange()
    {
        var instrument = Instrument.Create(1, "VTI", "Vanguard", Usd());

        instrument.Rename("Vanguard Total", "VTI");

        instrument.QuoteCurrency.ShouldBe("USD");
    }

    [Test]
    public void Rename_DoesNotRaiseEvents()
    {
        var instrument = Instrument.Create(1, "VTI", "Vanguard", Usd());
        instrument.ClearDomainEvents();

        instrument.Rename("Vanguard Total Stock Market ETF", "vti");

        instrument.Name.ShouldBe("Vanguard Total Stock Market ETF");
        instrument.Symbol.ShouldBe("VTI");
        instrument.DomainEvents.ShouldBeEmpty();
    }

    [Test]
    public void Disable_RaisesInstrumentDisabled()
    {
        var instrument = Instrument.Create(1, "VTI", "Vanguard", Usd());
        instrument.ClearDomainEvents();

        instrument.Disable();

        instrument.IsEnabled.ShouldBeFalse();
        instrument.DomainEvents.OfType<InstrumentDisabled>().ShouldHaveSingleItem();
    }

    [Test]
    public void Disable_WhenAlreadyDisabled_IsNoOp()
    {
        var instrument = Instrument.Create(1, "VTI", "Vanguard", Usd());
        instrument.Disable();
        instrument.ClearDomainEvents();

        instrument.Disable();

        instrument.IsEnabled.ShouldBeFalse();
        instrument.DomainEvents.ShouldBeEmpty();
    }

    [Test]
    public void Enable_FlipsAndRaisesInstrumentEnabledOnce()
    {
        var instrument = Instrument.Create(1, "VTI", "Vanguard", Usd());
        instrument.Disable();
        instrument.ClearDomainEvents();

        instrument.Enable();

        instrument.IsEnabled.ShouldBeTrue();
        instrument.DomainEvents.OfType<InstrumentEnabled>().ShouldHaveSingleItem();
    }

    [Test]
    public void Enable_WhenAlreadyEnabled_IsNoOp()
    {
        var instrument = Instrument.Create(1, "VTI", "Vanguard", Usd());
        instrument.ClearDomainEvents();

        instrument.Enable();

        instrument.IsEnabled.ShouldBeTrue();
        instrument.DomainEvents.ShouldBeEmpty();
    }

    [Test]
    public void Create_RejectsBlankName()
    {
        Should.Throw<DomainException>(() => Instrument.Create(1, "VTI", " ", Usd()));
        Should.Throw<DomainException>(() => Instrument.Create(1, "VTI", "", Usd()));
    }

    [Test]
    public void Create_RejectsInvalidSymbol()
    {
        Should.Throw<DomainException>(() => Instrument.Create(1, "", "Vanguard", Usd()));
        Should.Throw<DomainException>(() => Instrument.Create(1, "VT I", "Vanguard", Usd()));
        Should.Throw<DomainException>(() => Instrument.Create(1, "VTI!", "Vanguard", Usd()));
        Should.Throw<DomainException>(() => Instrument.Create(1, new string('A', 33), "Vanguard", Usd()));
    }

    [Test]
    public void Create_RejectsDisabledQuoteCurrency()
    {
        var disabled = Currency.Create("JPY", "Japanese Yen", 0, isEnabled: false);

        Should.Throw<DomainException>(() => Instrument.Create(1, "VTI", "Vanguard", disabled));
    }

    [Test]
    public void Create_RejectsEmptyQuoteCurrency()
    {
        Should.Throw<DomainException>(() => Instrument.Create(1, "VTI", "Vanguard", null!));
    }

    private static Currency Usd() => Currency.Create("USD", "United States Dollar", 2);

    private static Currency Nzd() => Currency.Create("NZD", "New Zealand Dollar", 2);
}
