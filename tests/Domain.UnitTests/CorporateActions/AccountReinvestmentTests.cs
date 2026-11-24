using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using MyWealthV2.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.CorporateActions;

/// <summary>Dividends R17: the standing reinvestment election of an Account.</summary>
public class AccountReinvestmentTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 25, 3, 0, 0, TimeSpan.Zero);

    private static Account Open(CustodyMode mode) =>
        Account.Create(1, 2, "Brokerage", AccountType.Brokerage, mode, Currency.Create("USD", "US Dollar", 2),
            mode == CustodyMode.Custodial && DepositReference.TryParse("K7M3Q9PXB", out var reference) ? reference : null);

    [Test]
    public void ANewAccount_DoesNotReinvest() => Open(CustodyMode.Custodial).ReinvestDividends.ShouldBeFalse();

    [Test]
    public void SetDividendReinvestment_OnAnOpenCustodialAccount_KeepsWhoAndWhen()
    {
        var account = Open(CustodyMode.Custodial);

        account.SetDividendReinvestment(true, 9, At);

        (account.ReinvestDividends, account.ReinvestDividendsChangedByUserId, account.ReinvestDividendsChangedAt)
            .ShouldBe((true, (int?)9, (DateTimeOffset?)At));
    }

    [Test]
    public void SetDividendReinvestment_OnAnExternalOrAClosedAccount_Throws()
    {
        Should.Throw<DomainException>(() => Open(CustodyMode.External).SetDividendReinvestment(true, 9, At));
        var closed = Open(CustodyMode.Custodial);
        closed.Close();
        Should.Throw<DomainException>(() => closed.SetDividendReinvestment(true, 9, At));
    }
}
