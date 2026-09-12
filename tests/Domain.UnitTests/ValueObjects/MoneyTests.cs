using MyWealthV2.Domain.Exceptions;
using MyWealthV2.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.ValueObjects;

public class MoneyTests
{
    [Test]
    public void Add_SameCurrency_SumsAmounts()
    {
        var left = Money.Create(10.25m, "nzd");
        var right = Money.Create(1.75m, "NZD");

        var sum = left.Add(right);

        sum.Amount.ShouldBe(12.00m);
        sum.Currency.ShouldBe("NZD");
    }

    [Test]
    public void Subtract_SameCurrency_SubtractsAmounts()
    {
        var left = Money.Create(10m, "USD");
        var right = Money.Create(2.5m, "USD");

        left.Subtract(right).Amount.ShouldBe(7.5m);
    }

    [Test]
    public void Add_CrossCurrency_IsRejected()
    {
        var nzd = Money.Create(10m, "NZD");
        var aud = Money.Create(10m, "AUD");

        Should.Throw<DomainException>(() => nzd.Add(aud));
    }

    [Test]
    public void Subtract_CrossCurrency_IsRejected()
    {
        var nzd = Money.Create(10m, "NZD");
        var aud = Money.Create(1m, "AUD");

        Should.Throw<DomainException>(() => nzd.Subtract(aud));
    }

    [Test]
    public void Equality_IsCurrencyAndAmount()
    {
        var left = Money.Create(10m, "nzd");
        var right = Money.Create(10m, "NZD");
        var differentAmount = Money.Create(11m, "NZD");
        var differentCurrency = Money.Create(10m, "AUD");

        left.ShouldBe(right);
        left.ShouldNotBe(differentAmount);
        left.ShouldNotBe(differentCurrency);
    }
}
