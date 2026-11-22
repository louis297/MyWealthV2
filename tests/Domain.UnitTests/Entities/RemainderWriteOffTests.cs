using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using MyWealthV2.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Entities;

/// <summary>Account closure R18, R24: <c>Transaction.PostRemainderWriteOff</c> and the source kind
/// <c>AccountClosure</c>.</summary>
public class RemainderWriteOffTests
{
    private const int TenantId = 3;
    private const int AccountId = 11;
    private static readonly DateTimeOffset BookedAt = new(2026, 11, 22, 3, 12, 40, TimeSpan.Zero);
    private static readonly Guid ClosureId = Guid.Parse("6b1f0000-0000-0000-0000-0000000000c1");

    [Test]
    public void AccountClosureSource_NeedsTheClosuresId_AndKeepsIt()
    {
        Should.Throw<DomainException>(() => TransactionSource.AccountClosure(Guid.Empty));
        var source = TransactionSource.AccountClosure(ClosureId);
        source.Kind.ShouldBe(TransactionSourceKind.AccountClosure);
        source.PublicId.ShouldBe(ClosureId);
        ((int)TransactionSourceKind.AccountClosure).ShouldBe(5);
    }

    [Test]
    public void PostRemainderWriteOff_IsOneNegativeCloseOutLeg_WithTheClosureSource_AndTheMemo()
    {
        var writeOff = WriteOff(0.004m, 2);

        writeOff.Type.ShouldBe(TransactionType.CloseOut);
        writeOff.TenantId.ShouldBe(TenantId);
        writeOff.BookedAt.ShouldBe(BookedAt);
        writeOff.Memo.ShouldBe("Account closure remainder");
        writeOff.Reference.ShouldBeNull();
        writeOff.Source.ShouldBe(TransactionSource.AccountClosure(ClosureId));
        var leg = writeOff.CashLegs.ShouldHaveSingleItem();
        leg.AccountId.ShouldBe(AccountId);
        leg.Amount.ShouldBe(-0.004m);
        leg.Currency.ShouldBe("USD");
        leg.Role.ShouldBe(CashLegRole.Principal);
        writeOff.SecurityLegs.ShouldBeEmpty();
        writeOff.PaymentDetail.ShouldBeNull();
    }

    [Test]
    public void PostRemainderWriteOff_TakesARemainderBelowOneOfACurrencyWithNoDecimals() =>
        WriteOff(0.4m, 0).CashLegs.Single().Amount.ShouldBe(-0.4m);

    [TestCase("0", 2)]
    [TestCase("-0.004", 2)]
    [TestCase("0.01", 2)]
    [TestCase("5.00", 2)]
    [TestCase("1", 0)]
    public void PostRemainderWriteOff_RefusesACashSumThatIsNotARemainder(string cashSum, int decimalPlaces) =>
        Should.Throw<DomainException>(() => WriteOff(decimal.Parse(cashSum), decimalPlaces));

    [Test]
    public void PostRemainderWriteOff_RefusesAnotherSource()
    {
        Should.Throw<DomainException>(() => Transaction.PostRemainderWriteOff(
            TenantId, AccountId, "USD", 2, 0.004m, BookedAt, TransactionSource.Manual));
        Should.Throw<DomainException>(() => Transaction.PostRemainderWriteOff(
            TenantId, AccountId, "USD", 2, 0.004m, BookedAt, TransactionSource.Payment(ClosureId)));
    }

    private static Transaction WriteOff(decimal cashSum, int decimalPlaces) =>
        Transaction.PostRemainderWriteOff(
            TenantId, AccountId, "USD", decimalPlaces, cashSum, BookedAt, TransactionSource.AccountClosure(ClosureId));
}
