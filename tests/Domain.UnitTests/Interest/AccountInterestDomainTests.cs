using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using MyWealthV2.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Interest;

/// <summary>Interest-run R18, R19, R22: the accrual row and the account's cursor.</summary>
public class AccountInterestDomainTests
{
    private static readonly DateOnly Day = new(2026, 12, 1);

    private static Account Account(CustodyMode mode) =>
        Domain.Entities.Account.Create(1, 2, "Managed", AccountType.Brokerage, mode, Currency.Create("USD", "US Dollar", 2),
            mode == CustodyMode.Custodial ? Reference() : null);

    private static DepositReference Reference() =>
        DepositReference.TryParse("K7M3Q9PXB", out var reference) ? reference : throw new InvalidOperationException();

    [Test]
    public void MarkInterestAccrued_MovesForward_AndNeverBack()
    {
        var account = Account(CustodyMode.Custodial);
        account.InterestAccruedThrough.ShouldBeNull();

        account.MarkInterestAccrued(Day);
        account.MarkInterestAccrued(Day);
        account.MarkInterestAccrued(Day.AddDays(5));

        account.InterestAccruedThrough.ShouldBe(Day.AddDays(5));
        Should.Throw<DomainException>(() => account.MarkInterestAccrued(Day.AddDays(4)));
        account.InterestAccruedThrough.ShouldBe(Day.AddDays(5));
    }

    [Test]
    public void MarkInterestAccrued_OnAnExternalAccount_IsRefused()
    {
        var account = Account(CustodyMode.External);

        Should.Throw<DomainException>(() => account.MarkInterestAccrued(Day));
        account.InterestAccruedThrough.ShouldBeNull();
    }

    [Test]
    public void Create_SetsFields_AndIsNotPosted()
    {
        var row = InterestAccrual.Create(1, 11, Day, 5000m, 4, 3.0000m, 0.41095890m);

        (row.TenantId, row.AccountId, row.Date, row.Balance, row.InterestRateScheduleId, row.EffectiveRatePercent, row.Amount)
            .ShouldBe((1, 11, Day, 5000m, 4, 3m, 0.41095890m));
        row.PostedTransactionId.ShouldBeNull();
    }

    [TestCase(0, 0.1)]
    [TestCase(-5, 0.1)]
    [TestCase(5000, 0)]
    public void Create_ANonPositiveBalanceOrAmount_IsRefused(decimal balance, decimal amount)
    {
        Should.Throw<DomainException>(() => InterestAccrual.Create(1, 11, Day, balance, 4, 3m, amount));
    }

    [Test]
    public void MarkPosted_StampsOnce()
    {
        var row = InterestAccrual.Create(1, 11, Day, 5000m, 4, 3m, 0.41095890m);

        row.MarkPosted(90);

        row.PostedTransactionId.ShouldBe(90);
        Should.Throw<DomainException>(() => row.MarkPosted(91));
        row.PostedTransactionId.ShouldBe(90);
    }
}
