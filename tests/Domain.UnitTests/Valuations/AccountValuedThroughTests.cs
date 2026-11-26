using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using MyWealthV2.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Valuations;

/// <summary>Valuation-run R5: the valued-through date of a Custodial account moves one day at a time.</summary>
public class AccountValuedThroughTests
{
    private static readonly DateOnly Day = new(2026, 3, 2);

    private static Account Account(CustodyMode mode) =>
        Domain.Entities.Account.Create(1, 2, "Managed", AccountType.Brokerage, mode, Currency.Create("NZD", "New Zealand Dollar", 2),
            mode == CustodyMode.Custodial ? Reference() : null);

    private static DepositReference Reference() =>
        DepositReference.TryParse("K7M3Q9PXB", out var reference) ? reference : throw new InvalidOperationException();

    [Test]
    public void MarkValued_SetsTheFirstDay_ThenMovesOneDayAtATime()
    {
        var account = Account(CustodyMode.Custodial);
        account.ValuedThrough.ShouldBeNull();

        account.MarkValued(Day);
        account.MarkValued(Day.AddDays(1));
        account.MarkValued(Day.AddDays(2));

        account.ValuedThrough.ShouldBe(Day.AddDays(2));
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(2)]
    public void MarkValued_ADayThatIsNotTheNextOne_IsRefused(int offset)
    {
        var account = Account(CustodyMode.Custodial);
        account.MarkValued(Day);

        Should.Throw<DomainException>(() => account.MarkValued(Day.AddDays(offset)));
        account.ValuedThrough.ShouldBe(Day);
    }

    [Test]
    public void MarkValued_OnAnExternalAccount_IsRefused()
    {
        var account = Account(CustodyMode.External);

        Should.Throw<DomainException>(() => account.MarkValued(Day));
        account.ValuedThrough.ShouldBeNull();
    }
}
