using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Exceptions;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.InKindTransfers;

/// <summary>In-kind transfers R3–R5 (docs/features/in-kind-transfers.md §5): the safekeeping account.</summary>
public class SafekeepingAccountTests
{
    private const string Connection = "simulator";

    [Test]
    public void Create_IsActive_AndKeepsTrimmedDetails()
    {
        var account = SafekeepingAccount.Create(
            7, Usd(), "  Simulator Custody ", " North Advisory Nominees Ltd ", " SIM-DEMO-CUSTODY-USD ", Connection);

        account.TenantId.ShouldBe(7);
        account.Currency.ShouldBe("USD");
        account.CustodianName.ShouldBe("Simulator Custody");
        account.AccountName.ShouldBe("North Advisory Nominees Ltd");
        account.AccountNumber.ShouldBe("SIM-DEMO-CUSTODY-USD");
        account.CustodianConnection.ShouldBe(Connection);
        account.IsActive.ShouldBeTrue();
        account.PublicId.ShouldNotBe(Guid.Empty);
        account.DomainEvents.ShouldBeEmpty();
    }

    [Test]
    public void Create_UsesThePublicIdGiven()
    {
        var publicId = Guid.NewGuid();

        SafekeepingAccount.Create(1, Usd(), "Custodian", "Name", "123", Connection, publicId).PublicId.ShouldBe(publicId);
    }

    [TestCase("")]
    [TestCase("   ")]
    public void Create_RefusesABlankCustodianName(string name) =>
        Should.Throw<DomainException>(() => SafekeepingAccount.Create(1, Usd(), name, "Name", "123", Connection));

    [Test]
    public void Create_RefusesACustodianNameOver100() =>
        Should.Throw<DomainException>(() => SafekeepingAccount.Create(1, Usd(), new string('c', 101), "Name", "123", Connection));

    [Test]
    public void Create_RefusesAnAccountNameOver140() =>
        Should.Throw<DomainException>(() => SafekeepingAccount.Create(1, Usd(), "Custodian", new string('n', 141), "123", Connection));

    [TestCase("")]
    [TestCase("12/34")]
    [TestCase("12  34")]
    public void Create_RefusesABadAccountNumber(string number) =>
        Should.Throw<DomainException>(() => SafekeepingAccount.Create(1, Usd(), "Custodian", "Name", number, Connection));

    [TestCase("")]
    [TestCase("Simulator")]
    public void Create_RefusesABadConnectionName(string connection) =>
        Should.Throw<DomainException>(() => SafekeepingAccount.Create(1, Usd(), "Custodian", "Name", "123", connection));

    [Test]
    public void Create_RefusesADisabledCurrency() =>
        Should.Throw<DomainException>(() => SafekeepingAccount.Create(
            1, Currency.Create("JPY", "Japanese Yen", 0, isActive: false), "Custodian", "Name", "1", Connection));

    [Test]
    public void DisableThenEnable_ChangesOnlyTheFlag()
    {
        var account = SafekeepingAccount.Create(1, Usd(), "Custodian", "Name", "123", Connection);

        account.Disable();
        account.IsActive.ShouldBeFalse();
        account.Disable();
        account.IsActive.ShouldBeFalse();
        account.Enable();
        account.IsActive.ShouldBeTrue();
    }

    private static Currency Usd() => Currency.Create("USD", "US Dollar", 2);
}
