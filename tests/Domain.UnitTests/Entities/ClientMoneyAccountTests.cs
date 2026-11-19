using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Exceptions;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Entities;

/// <summary>Client money accounts R2, R7, R8, R10 (docs/features/client-money-accounts.md §5).</summary>
public class ClientMoneyAccountTests
{
    [Test]
    public void Create_IsActive_AndKeepsTrimmedDetails()
    {
        var account = ClientMoneyAccount.Create(
            7, Usd(), "  Simulator Bank ", " North Advisory Ltd Client Funds Trust Account ", " 02-0100-0123456-00 ");

        account.TenantId.ShouldBe(7);
        account.Currency.ShouldBe("USD");
        account.BankName.ShouldBe("Simulator Bank");
        account.AccountName.ShouldBe("North Advisory Ltd Client Funds Trust Account");
        account.AccountNumber.ShouldBe("02-0100-0123456-00");
        account.IsActive.ShouldBeTrue();
        account.PublicId.ShouldNotBe(Guid.Empty);
    }

    [Test]
    public void Create_UsesThePublicIdGiven()
    {
        var publicId = Guid.NewGuid();

        ClientMoneyAccount.Create(1, Usd(), "Bank", "Name", "123", publicId).PublicId.ShouldBe(publicId);
    }

    [Test]
    public void Create_RaisesNoDomainEvent()
    {
        ClientMoneyAccount.Create(1, Usd(), "Bank", "Name", "123").DomainEvents.ShouldBeEmpty();
    }

    [Test]
    public void Create_AcceptsEachFieldAtItsLimits()
    {
        var shortest = ClientMoneyAccount.Create(1, Usd(), "B", "N", "1");
        var longest = ClientMoneyAccount.Create(
            1, Usd(), new string('B', 100), new string('N', 140), "GB29 NWBK-6016 1331 9268 19ab" + "CDEFG");

        shortest.BankName.ShouldBe("B");
        longest.BankName.Length.ShouldBe(100);
        longest.AccountName.Length.ShouldBe(140);
        longest.AccountNumber.Length.ShouldBe(34);
    }

    [TestCase("")]
    [TestCase("   ")]
    public void Create_RejectsBlankBankName(string bankName) =>
        Should.Throw<DomainException>(() => ClientMoneyAccount.Create(1, Usd(), bankName, "Name", "123"));

    [Test]
    public void Create_RejectsBankNameOver100() =>
        Should.Throw<DomainException>(() => ClientMoneyAccount.Create(1, Usd(), new string('B', 101), "Name", "123"));

    [TestCase("")]
    [TestCase("   ")]
    public void Create_RejectsBlankAccountName(string accountName) =>
        Should.Throw<DomainException>(() => ClientMoneyAccount.Create(1, Usd(), "Bank", accountName, "123"));

    [Test]
    public void Create_RejectsAccountNameOver140() =>
        Should.Throw<DomainException>(() => ClientMoneyAccount.Create(1, Usd(), "Bank", new string('N', 141), "123"));

    [TestCase("")]
    [TestCase("   ")]
    public void Create_RejectsBlankAccountNumber(string accountNumber) =>
        Should.Throw<DomainException>(() => ClientMoneyAccount.Create(1, Usd(), "Bank", "Name", accountNumber));

    [Test]
    public void Create_RejectsAccountNumberOver34() =>
        Should.Throw<DomainException>(() => ClientMoneyAccount.Create(1, Usd(), "Bank", "Name", new string('1', 35)));

    [TestCase("02/0100")]
    [TestCase("02_0100")]
    [TestCase("02.0100")]
    [TestCase("ÄB12")]
    public void Create_RejectsBadCharacterInAccountNumber(string accountNumber) =>
        Should.Throw<DomainException>(() => ClientMoneyAccount.Create(1, Usd(), "Bank", "Name", accountNumber));

    [Test]
    public void Create_RejectsTwoSpacesInARowInAccountNumber() =>
        Should.Throw<DomainException>(() => ClientMoneyAccount.Create(1, Usd(), "Bank", "Name", "GB29  NWBK"));

    [Test]
    public void Create_RejectsDisabledCurrency() =>
        Should.Throw<DomainException>(() =>
            ClientMoneyAccount.Create(1, Currency.Create("JPY", "Japanese Yen", 0, isActive: false), "Bank", "Name", "1"));

    [Test]
    public void Disable_SetsInactive_AndIsIdempotent()
    {
        var account = ClientMoneyAccount.Create(1, Usd(), "Bank", "Name", "123");

        account.Disable();
        account.Disable();

        account.IsActive.ShouldBeFalse();
        account.DomainEvents.ShouldBeEmpty();
    }

    [Test]
    public void Enable_SetsActive_AndIsIdempotent()
    {
        var account = ClientMoneyAccount.Create(1, Usd(), "Bank", "Name", "123");
        account.Disable();

        account.Enable();
        account.Enable();

        account.IsActive.ShouldBeTrue();
        account.DomainEvents.ShouldBeEmpty();
    }

    private static Currency Usd() => Currency.Create("USD", "US Dollar", 2);
}
