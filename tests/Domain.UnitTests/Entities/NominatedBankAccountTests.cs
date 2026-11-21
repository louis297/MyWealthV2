using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using MyWealthV2.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Entities;

/// <summary>Withdrawals R2, R5–R9, R11, R12 (docs/features/withdrawals.md §5): the nominated bank account.</summary>
public class NominatedBankAccountTests
{
    private const int Adder = 11;
    private const int Reviewer = 22;
    private static readonly DateTimeOffset AddedAt = new(2026, 11, 20, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = AddedAt.AddMinutes(10);

    [Test]
    public void Add_IsPending_AndKeepsTrimmedDetails()
    {
        var publicId = Guid.NewGuid();

        var row = NominatedBankAccount.Add(
            7, 42, Usd(), "  ANZ ", " J Smith ", " 06-0123-0456789-00 ", Adder, AddedAt, publicId);

        row.TenantId.ShouldBe(7);
        row.CustomerId.ShouldBe(42);
        row.Currency.ShouldBe("USD");
        row.BankName.ShouldBe("ANZ");
        row.AccountName.ShouldBe("J Smith");
        row.AccountNumber.ShouldBe("06-0123-0456789-00");
        row.AccountNumberKey.ShouldBe("060123045678900");
        row.Status.ShouldBe(NominatedBankAccountStatus.Pending);
        row.AddedByUserId.ShouldBe(Adder);
        row.AddedAt.ShouldBe(AddedAt);
        row.PublicId.ShouldBe(publicId);
        row.ReviewedByUserId.ShouldBeNull();
        row.RemovedByUserId.ShouldBeNull();
        row.DomainEvents.ShouldBeEmpty();
    }

    [Test]
    public void Add_DrawsAPublicId_WhenNoneIsGiven() =>
        Add().PublicId.ShouldNotBe(Guid.Empty);

    [Test]
    public void Add_AcceptsEachFieldAtItsLimits()
    {
        var longest = NominatedBankAccount.Add(
            1, 2, Usd(), new string('B', 100), new string('N', 140), "GB29 NWBK-6016 1331 9268 19ab" + "CDEFG", Adder, AddedAt);

        longest.BankName.Length.ShouldBe(100);
        longest.AccountName.Length.ShouldBe(140);
        longest.AccountNumber.Length.ShouldBe(34);
    }

    [TestCase("")]
    [TestCase("   ")]
    public void Add_RejectsABlankBankName(string bankName) =>
        Should.Throw<DomainException>(() => NominatedBankAccount.Add(1, 2, Usd(), bankName, "Name", "123", Adder, AddedAt));

    [Test]
    public void Add_RejectsABankNameOver100() =>
        Should.Throw<DomainException>(() => NominatedBankAccount.Add(1, 2, Usd(), new string('B', 101), "Name", "123", Adder, AddedAt));

    [TestCase("")]
    [TestCase("   ")]
    public void Add_RejectsABlankAccountName(string accountName) =>
        Should.Throw<DomainException>(() => NominatedBankAccount.Add(1, 2, Usd(), "Bank", accountName, "123", Adder, AddedAt));

    [Test]
    public void Add_RejectsAnAccountNameOver140() =>
        Should.Throw<DomainException>(() => NominatedBankAccount.Add(1, 2, Usd(), "Bank", new string('N', 141), "123", Adder, AddedAt));

    [TestCase("")]
    [TestCase("12  34")]
    [TestCase("12_34")]
    [TestCase("1234567890123456789012345678901234X")]
    public void Add_RejectsABadAccountNumber(string accountNumber) =>
        Should.Throw<DomainException>(() => NominatedBankAccount.Add(1, 2, Usd(), "Bank", "Name", accountNumber, Adder, AddedAt));

    [Test]
    public void Add_RejectsADisabledCurrency() =>
        Should.Throw<DomainException>(() => NominatedBankAccount.Add(
            1, 2, Currency.Create("USD", "US Dollar", 2, isActive: false), "Bank", "Name", "123", Adder, AddedAt));

    [TestCase("06-0123-0456789-00", "060123045678900")]
    [TestCase("gb29 nwbk 6016", "GB29NWBK6016")]
    public void KeyOf_UpperCases_AndDropsSpacesAndHyphens(string number, string key) =>
        NominatedBankAccount.KeyOf(number).ShouldBe(key);

    [Test]
    public void Verify_FromPending_ByAnotherPerson_RecordsTheReview()
    {
        var row = Add();

        row.Verify(Reviewer, "  Bank statement sighted 2026-11-20 ", Later);

        row.Status.ShouldBe(NominatedBankAccountStatus.Verified);
        row.ReviewedByUserId.ShouldBe(Reviewer);
        row.ReviewedAt.ShouldBe(Later);
        row.ReviewNote.ShouldBe("Bank statement sighted 2026-11-20");
    }

    [Test]
    public void Reject_FromPending_ByAnotherPerson_RecordsTheReview()
    {
        var row = Add();

        row.Reject(Reviewer, "Name does not match", Later);

        row.Status.ShouldBe(NominatedBankAccountStatus.Rejected);
        row.ReviewedByUserId.ShouldBe(Reviewer);
        row.ReviewedAt.ShouldBe(Later);
        row.ReviewNote.ShouldBe("Name does not match");
    }

    [Test]
    public void Verify_ByThePersonWhoAddedIt_Throws()
    {
        var row = Add();

        Should.Throw<NominatedBankAccountStatusException>(() => row.Verify(Adder, "Checked", Later)).Message
            .ShouldBe("A bank account must be verified by someone other than the person who added it.");
        row.Status.ShouldBe(NominatedBankAccountStatus.Pending);
    }

    [Test]
    public void Reject_ByThePersonWhoAddedIt_Throws()
    {
        var row = Add();

        Should.Throw<NominatedBankAccountStatusException>(() => row.Reject(Adder, "No", Later)).Message
            .ShouldBe("A bank account must be verified by someone other than the person who added it.");
        row.Status.ShouldBe(NominatedBankAccountStatus.Pending);
    }

    [Test]
    public void Remove_FromPending_RecordsTheRemoval_AndNoReview()
    {
        var row = Add();

        row.Remove(Adder, " Customer closed it ", Later);

        row.Status.ShouldBe(NominatedBankAccountStatus.Removed);
        row.RemovedByUserId.ShouldBe(Adder);
        row.RemovedAt.ShouldBe(Later);
        row.RemovedNote.ShouldBe("Customer closed it");
        row.ReviewedByUserId.ShouldBeNull();
    }

    [Test]
    public void Remove_FromVerified_KeepsTheReview()
    {
        var row = Add();
        row.Verify(Reviewer, "Checked", Later);

        row.Remove(Reviewer, "Customer closed it", Later.AddDays(1));

        row.Status.ShouldBe(NominatedBankAccountStatus.Removed);
        row.ReviewedByUserId.ShouldBe(Reviewer);
        row.ReviewNote.ShouldBe("Checked");
        row.RemovedByUserId.ShouldBe(Reviewer);
    }

    [TestCase(NominatedBankAccountStatus.Verified, "verified")]
    [TestCase(NominatedBankAccountStatus.Rejected, "verified")]
    [TestCase(NominatedBankAccountStatus.Removed, "verified")]
    [TestCase(NominatedBankAccountStatus.Verified, "rejected")]
    [TestCase(NominatedBankAccountStatus.Rejected, "rejected")]
    [TestCase(NominatedBankAccountStatus.Removed, "rejected")]
    [TestCase(NominatedBankAccountStatus.Rejected, "removed")]
    [TestCase(NominatedBankAccountStatus.Removed, "removed")]
    public void AnActionFromAnotherStatus_Throws_WithTheR8Message(NominatedBankAccountStatus status, string action)
    {
        var row = InStatus(status);
        Action act = action switch
        {
            "verified" => () => row.Verify(Reviewer, "Note", Later),
            "rejected" => () => row.Reject(Reviewer, "Note", Later),
            _ => () => row.Remove(Reviewer, "Note", Later)
        };

        Should.Throw<NominatedBankAccountStatusException>(act).Message
            .ShouldBe($"This bank account is {status} and cannot be {action}.");
        row.Status.ShouldBe(status);
    }

    [TestCase("")]
    [TestCase("   ")]
    public void EachAction_RequiresANote(string note)
    {
        Should.Throw<DomainException>(() => Add().Verify(Reviewer, note, Later));
        Should.Throw<DomainException>(() => Add().Reject(Reviewer, note, Later));
        Should.Throw<DomainException>(() => Add().Remove(Reviewer, note, Later));
    }

    [Test]
    public void EachAction_RefusesANoteOver200()
    {
        var note = new string('n', 201);

        Should.Throw<DomainException>(() => Add().Verify(Reviewer, note, Later));
        Should.Throw<DomainException>(() => Add().Reject(Reviewer, note, Later));
        Should.Throw<DomainException>(() => Add().Remove(Reviewer, note, Later));
        Add().Verify(Reviewer, new string('n', 200), Later);
    }

    [Test]
    public void TheDetails_HaveNoPublicSetter()
    {
        foreach (var name in new[] { "Currency", "BankName", "AccountName", "AccountNumber", "AccountNumberKey", "CustomerId" })
        {
            typeof(NominatedBankAccount).GetProperty(name)!.SetMethod!.IsPublic.ShouldBeFalse(name);
        }
    }

    private static NominatedBankAccount Add() =>
        NominatedBankAccount.Add(1, 2, Usd(), "ANZ", "J Smith", "06-0123-0456789-00", Adder, AddedAt);

    private static NominatedBankAccount InStatus(NominatedBankAccountStatus status)
    {
        var row = Add();
        switch (status)
        {
            case NominatedBankAccountStatus.Verified:
                row.Verify(Reviewer, "Checked", Later);
                break;
            case NominatedBankAccountStatus.Rejected:
                row.Reject(Reviewer, "No", Later);
                break;
            case NominatedBankAccountStatus.Removed:
                row.Remove(Reviewer, "Gone", Later);
                break;
        }

        return row;
    }

    private static Currency Usd() => Currency.Create("USD", "US Dollar", 2);
}
