using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Exceptions;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Entities;

public class CurrencyTests
{
    [Test]
    public void Create_NormalisesCodeToUpperCase()
    {
        var currency = Currency.Create("nzd", "New Zealand Dollar", 2);

        currency.Code.ShouldBe("NZD");
        currency.Name.ShouldBe("New Zealand Dollar");
        currency.DecimalPlaces.ShouldBe(2);
        currency.IsActive.ShouldBeTrue();
    }

    [Test]
    public void Create_RejectsCodeThatIsNotThreeLetters()
    {
        Should.Throw<DomainException>(() => Currency.Create("NZ", "New Zealand Dollar", 2));
        Should.Throw<DomainException>(() => Currency.Create("NZDX", "New Zealand Dollar", 2));
        Should.Throw<DomainException>(() => Currency.Create("N1D", "New Zealand Dollar", 2));
        Should.Throw<DomainException>(() => Currency.Create(" ", "New Zealand Dollar", 2));
    }

    [Test]
    public void Create_RejectsDecimalPlacesOutsideZeroToFour()
    {
        Should.Throw<DomainException>(() => Currency.Create("NZD", "New Zealand Dollar", -1));
        Should.Throw<DomainException>(() => Currency.Create("NZD", "New Zealand Dollar", 5));
    }

    [Test]
    public void Create_CanBeDisabled()
    {
        var currency = Currency.Create("JPY", "Japanese Yen", 0, isEnabled: false);

        currency.IsActive.ShouldBeFalse();
        currency.DecimalPlaces.ShouldBe(0);
    }
}
