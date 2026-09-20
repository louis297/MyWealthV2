using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Events;
using MyWealthV2.Domain.Exceptions;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Entities;

public class AccountTests
{
    [Test]
    public void Create_StoresTypeAndCurrencyAndOpens()
    {
        var account = Account.Create(1, 2, "Everyday spending", AccountType.Bank, Nzd());

        account.TenantId.ShouldBe(1);
        account.CustomerId.ShouldBe(2);
        account.Name.ShouldBe("Everyday spending");
        account.Type.ShouldBe(AccountType.Bank);
        account.Currency.ShouldBe("NZD");
        account.Status.ShouldBe(AccountStatus.Open);
        account.IsActive.ShouldBeTrue();
    }

    [Test]
    public void Create_RaisesAccountOpened()
    {
        var account = Account.Create(1, 2, "Everyday spending", AccountType.Bank, Nzd());

        account.DomainEvents.OfType<AccountOpened>().ShouldHaveSingleItem();
    }

    [Test]
    public void Create_TrimsNameAndPreservesCasing()
    {
        var account = Account.Create(1, 2, "  Everyday Spending  ", AccountType.Cash, Nzd());

        account.Name.ShouldBe("Everyday Spending");
    }

    [Test]
    public void Create_AllowsReservedTypesOnTheEntity()
    {
        var property = Account.Create(1, 2, "House", AccountType.Property, Nzd());
        var credit = Account.Create(1, 2, "Card", AccountType.Credit, Nzd());

        property.Type.ShouldBe(AccountType.Property);
        credit.Type.ShouldBe(AccountType.Credit);
    }

    [Test]
    public void TypeAndCurrency_CannotChange()
    {
        var account = Account.Create(1, 2, "Everyday spending", AccountType.Bank, Nzd());

        account.Rename("Renamed");

        account.Type.ShouldBe(AccountType.Bank);
        account.Currency.ShouldBe("NZD");
    }

    [Test]
    public void Rename_DoesNotRaiseEvents()
    {
        var account = Account.Create(1, 2, "Everyday spending", AccountType.Bank, Nzd());
        account.ClearDomainEvents();

        account.Rename("  Everyday  ");

        account.Name.ShouldBe("Everyday");
        account.DomainEvents.ShouldBeEmpty();
    }

    [Test]
    public void Close_RaisesAccountClosed()
    {
        var account = Account.Create(1, 2, "Everyday spending", AccountType.Bank, Nzd());
        account.ClearDomainEvents();

        account.Close();

        account.Status.ShouldBe(AccountStatus.Closed);
        account.IsActive.ShouldBeFalse();
        account.DomainEvents.OfType<AccountClosed>().ShouldHaveSingleItem();
    }

    [Test]
    public void Close_WhenAlreadyClosed_IsNoOp()
    {
        var account = Account.Create(1, 2, "Everyday spending", AccountType.Bank, Nzd());
        account.Close();
        account.ClearDomainEvents();

        account.Close();

        account.Status.ShouldBe(AccountStatus.Closed);
        account.DomainEvents.ShouldBeEmpty();
    }

    [Test]
    public void Reopen_FlipsAndRaisesAccountReopenedOnce()
    {
        var account = Account.Create(1, 2, "Everyday spending", AccountType.Bank, Nzd());
        account.Close();
        account.ClearDomainEvents();

        account.Reopen();

        account.Status.ShouldBe(AccountStatus.Open);
        account.IsActive.ShouldBeTrue();
        account.DomainEvents.OfType<AccountReopened>().ShouldHaveSingleItem();
    }

    [Test]
    public void Reopen_WhenAlreadyOpen_IsNoOp()
    {
        var account = Account.Create(1, 2, "Everyday spending", AccountType.Bank, Nzd());
        account.ClearDomainEvents();

        account.Reopen();

        account.Status.ShouldBe(AccountStatus.Open);
        account.DomainEvents.ShouldBeEmpty();
    }

    [Test]
    public void Create_RejectsBlankName()
    {
        Should.Throw<DomainException>(() => Account.Create(1, 2, " ", AccountType.Bank, Nzd()));
        Should.Throw<DomainException>(() => Account.Create(1, 2, "", AccountType.Bank, Nzd()));
    }

    [Test]
    public void Rename_RejectsBlankName()
    {
        var account = Account.Create(1, 2, "Everyday spending", AccountType.Bank, Nzd());

        Should.Throw<DomainException>(() => account.Rename(" "));
        Should.Throw<DomainException>(() => account.Rename(""));
    }

    [Test]
    public void Create_RejectsEmptyCurrency()
    {
        Should.Throw<DomainException>(() => Account.Create(1, 2, "Everyday spending", AccountType.Bank, null!));
    }

    private static Currency Nzd() => Currency.Create("NZD", "New Zealand Dollar", 2);
}
