using MyWealthV2.Domain.CorporateActions;
using MyWealthV2.Domain.Custody;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using MyWealthV2.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.CorporateActions;

/// <summary>Corporate-actions R26, R27, R32, R33: <c>Transaction.PostCorporateAction</c>, the corporate action detail
/// and the source kind <c>CorporateAction</c>.</summary>
public class CorporateActionPostingTests
{
    private const int TenantId = 3;
    private const int AccountId = 11;
    private const int InstrumentId = 21;
    private static readonly DateTimeOffset BookedAt = new(2026, 11, 22, 3, 41, 2, TimeSpan.Zero);
    private static readonly DateOnly ExDate = new(2026, 11, 24);
    private static readonly Guid ActionId = Guid.Parse("5a1c0000-0000-0000-0000-0000000000c7");

    private static Transaction Post(
        CorporateActionKind kind = CorporateActionKind.Split,
        int ratioNew = 2,
        int ratioHeld = 1,
        decimal before = 100m,
        decimal after = 200m,
        TransactionSource? source = null) =>
        Transaction.PostCorporateAction(
            TenantId, AccountId, InstrumentId, "USD", kind, new CorporateActionRatio(ratioNew, ratioHeld), ExDate,
            new DateOnly(2026, 11, 25), null, before, after, BookedAt, source ?? TransactionSource.CorporateAction(ActionId));

    [Test]
    public void CorporateActionSource_NeedsTheActionsId_AndKeepsIt()
    {
        Should.Throw<DomainException>(() => TransactionSource.CorporateAction(Guid.Empty));
        var source = TransactionSource.CorporateAction(ActionId);
        source.Kind.ShouldBe(TransactionSourceKind.CorporateAction);
        source.PublicId.ShouldBe(ActionId);
        ((int)TransactionSourceKind.CorporateAction).ShouldBe(7);
    }

    [Test]
    public void CustodyRules_ACorporateActionPosting_IsNotHandReversible_AndHandSplitStaysRefused()
    {
        CustodyRules.AllowsHandReverse(CustodyMode.Custodial, TransactionSourceKind.CorporateAction).ShouldBeFalse();
        CustodyRules.AllowsHandReverse(CustodyMode.External, TransactionSourceKind.CorporateAction).ShouldBeTrue();
        CustodyRules.AllowsManualEntry(CustodyMode.Custodial, TransactionType.Split).ShouldBeFalse();
        CustodyRules.AllowsManualEntry(CustodyMode.Custodial, TransactionType.Bonus).ShouldBeFalse();
        CustodyRules.AllowsManualEntry(CustodyMode.Custodial, TransactionType.Scrip).ShouldBeFalse();
    }

    [Test]
    public void ASplit_IsOneSecurityLegOfTheDifference_WithCostZero_AndNoCashLeg()
    {
        var posting = Post();

        posting.Type.ShouldBe(TransactionType.Split);
        posting.CashLegs.ShouldBeEmpty();
        var leg = posting.SecurityLegs.ShouldHaveSingleItem();
        (leg.TenantId, leg.AccountId, leg.InstrumentId, leg.Quantity, leg.CostAmount, leg.CostCurrency)
            .ShouldBe((TenantId, AccountId, InstrumentId, 100m, 0m, "USD"));
        posting.Memo.ShouldBe("Split 2 for 1");
        posting.Reference.ShouldBeNull();
        posting.BookedAt.ShouldBe(BookedAt);
        (posting.SourceKind, posting.SourcePublicId).ShouldBe((TransactionSourceKind.CorporateAction, (Guid?)ActionId));
        posting.PublicId.ShouldNotBe(Guid.Empty);
        posting.DomainEvents.ShouldContain(e => e is MyWealthV2.Domain.Events.TransactionPosted);
    }

    [Test]
    public void AConsolidation_IsPostedAsSplit_WithANegativeLeg()
    {
        var posting = Post(CorporateActionKind.Consolidation, 1, 10, 105m, 10.5m);

        posting.Type.ShouldBe(TransactionType.Split);
        posting.SecurityLegs.Single().Quantity.ShouldBe(-94.5m);
        posting.Memo.ShouldBe("Consolidation 1 for 10");
    }

    [Test]
    public void ABonus_IsPostedAsBonus()
    {
        var posting = Post(CorporateActionKind.Bonus, 1, 5, 12.34567891m, 14.81481469m);

        posting.Type.ShouldBe(TransactionType.Bonus);
        posting.SecurityLegs.Single().Quantity.ShouldBe(2.46913578m);
        posting.Memo.ShouldBe("Bonus 1 for 5");
    }

    [Test]
    public void TheDetail_KeepsTheEventAndBothQuantities()
    {
        var detail = Post().CorporateActionDetail.ShouldNotBeNull();

        (detail.TenantId, detail.Kind, detail.RatioNew, detail.RatioHeld, detail.ExDate, detail.RecordDate, detail.PaymentDate)
            .ShouldBe((TenantId, CorporateActionKind.Split, 2, 1, ExDate, (DateOnly?)new DateOnly(2026, 11, 25), (DateOnly?)null));
        (detail.QuantityBefore, detail.QuantityAfter).ShouldBe((100m, 200m));
    }

    [Test]
    public void ASourceOtherThanCorporateAction_Throws()
    {
        Should.Throw<DomainException>(() => Post(source: TransactionSource.Manual));
        Should.Throw<DomainException>(() => Post(source: TransactionSource.InterestRun(Guid.NewGuid())));
    }

    [TestCase(100, 0)]
    [TestCase(100, 100)]
    [TestCase(0, 10)]
    [TestCase(100, -5)]
    public void AQuantityAfterOfZeroOrUnchanged_OrNoHoldingBefore_Throws(decimal before, decimal after) =>
        Should.Throw<DomainException>(() => Post(before: before, after: after));

    [Test]
    public void ARatioThatDoesNotFitTheKind_Throws() =>
        Should.Throw<DomainException>(() => Post(CorporateActionKind.Split, 1, 2, 100m, 50m));

    [Test]
    public void OtherFactories_HaveNoCorporateActionDetail()
    {
        Transaction.PostSplit(
                TenantId, AccountId, AccountType.Brokerage, TransactionType.Split, InstrumentId, 200m, "USD", BookedAt, null, null,
                TransactionSource.Manual, 100m, 500m)
            .CorporateActionDetail.ShouldBeNull();
    }

    [Test]
    public void Reverse_CopiesTheDetail_AndTheOppositeLeg()
    {
        var posting = Post();

        var reversal = posting.Reverse(
            TransactionSource.CorporateAction(ActionId),
            BookedAt.AddDays(1),
            new Dictionary<int, decimal>(),
            new Dictionary<(int AccountId, int InstrumentId), decimal> { [(AccountId, InstrumentId)] = 200m });

        reversal.SecurityLegs.Single().Quantity.ShouldBe(-100m);
        var detail = reversal.CorporateActionDetail.ShouldNotBeNull();
        detail.ShouldNotBeSameAs(posting.CorporateActionDetail);
        (detail.Kind, detail.RatioNew, detail.RatioHeld, detail.ExDate, detail.QuantityBefore, detail.QuantityAfter)
            .ShouldBe((CorporateActionKind.Split, 2, 1, ExDate, 100m, 200m));
    }
}
