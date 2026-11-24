using MyWealthV2.Domain.CorporateActions;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.CorporateActions;

/// <summary>Dividends R2, R3, R7, R9, R11, R14 and §5: a dividend as a corporate action, its entitlements and its
/// announcement.</summary>
public class CorporateActionDividendTests
{
    private static readonly DateOnly ExDate = new(2026, 12, 8);
    private static readonly DateTimeOffset At = new(2026, 11, 24, 3, 41, 0, TimeSpan.Zero);

    private static readonly DividendTerms Cash = DividendTerms.Create(0.72m, DividendTaxSource.NewZealand, 0.28m, null, null);
    private static readonly DividendTerms Plan = DividendTerms.Create(1.00m, DividendTaxSource.Foreign, null, 15m, 25m);

    private static CorporateAction Manual() =>
        CorporateAction.EnterDividend(1, 10, Cash, ExDate, ExDate, new DateOnly(2026, 12, 15), " Quarterly ");

    private static CorporateAction Feed(int version = 1) =>
        CorporateAction.AnnounceDividend(1, 10, "simulator", "w-CA3", version, Plan, ExDate, null, new DateOnly(2026, 12, 15));

    [Test]
    public void EnterDividend_IsAManualAnnouncedDividend_WithTermsAndNoRatio()
    {
        var action = Manual();

        (action.Kind, action.Origin, action.Status).ShouldBe(
            (CorporateActionKind.Dividend, CorporateActionOrigin.Manual, CorporateActionStatus.Announced));
        action.RatioNew.ShouldBeNull();
        action.RatioHeld.ShouldBeNull();
        action.Terms.ShouldBe(Cash);
        (action.AmountPerShare, action.TaxSource, action.ImputationCreditPerShare, action.ForeignTaxRatePercent,
                action.ReinvestmentPrice)
            .ShouldBe(((decimal?)0.72m, (DividendTaxSource?)DividendTaxSource.NewZealand, (decimal?)0.28m, (decimal?)0m,
                (decimal?)null));
        (action.ExDate, action.RecordDate, action.PaymentDate).ShouldBe(
            (ExDate, (DateOnly?)ExDate, (DateOnly?)new DateOnly(2026, 12, 15)));
        action.Memo.ShouldBe("Quarterly");
    }

    [Test]
    public void Ratio_OfADividend_Throws() =>
        Should.Throw<DomainException>(() => Manual().Ratio);

    [Test]
    public void Terms_OfASplit_IsNull() =>
        CorporateAction.Enter(1, 10, CorporateActionKind.Split, new CorporateActionRatio(2, 1), ExDate, null, null, null)
            .Terms.ShouldBeNull();

    [Test]
    public void EnterDividend_WithAReinvestmentPrice_Throws() =>
        Should.Throw<DomainException>(() => CorporateAction.EnterDividend(1, 10, Plan, ExDate, null, null, null));

    [Test]
    public void Enter_AndAnnounce_WithKindDividend_Throw()
    {
        Should.Throw<DomainException>(() => CorporateAction.Enter(
            1, 10, CorporateActionKind.Dividend, new CorporateActionRatio(2, 1), ExDate, null, null, null));
        Should.Throw<DomainException>(() => CorporateAction.Announce(
            1, 10, "simulator", "w-CA1", 1, CorporateActionKind.Dividend, new CorporateActionRatio(2, 1), ExDate, null, null));
    }

    [Test]
    public void AnnounceDividend_IsAFeedDividend_AndKeepsThePlanPrice()
    {
        var action = Feed(2);

        (action.Kind, action.Origin, action.Connection, action.PartyActionId, action.FeedVersion).ShouldBe(
            (CorporateActionKind.Dividend, CorporateActionOrigin.Feed, "simulator", "w-CA3", (int?)2));
        action.Terms.ShouldBe(Plan);
    }

    [Test]
    public void ChangeDividend_ReplacesTheTermsAndDates_OfAnAnnouncedManualDividend()
    {
        var action = Manual();
        var terms = DividendTerms.Create(0.26m, DividendTaxSource.Foreign, null, 15m, null);

        action.ChangeDividend(terms, ExDate.AddDays(7), null, null, null);

        action.Terms.ShouldBe(terms);
        action.ExDate.ShouldBe(ExDate.AddDays(7));
        action.RecordDate.ShouldBeNull();
        action.Memo.ShouldBeNull();
    }

    [Test]
    public void ChangeDividend_WithAPlan_OrOnASplit_OrOnAFeedDividend_Throws()
    {
        Should.Throw<DomainException>(() => Manual().ChangeDividend(Plan, ExDate, null, null, null));
        var split = CorporateAction.Enter(1, 10, CorporateActionKind.Split, new CorporateActionRatio(2, 1), ExDate, null, null, null);
        Should.Throw<DomainException>(() => split.ChangeDividend(Cash, ExDate, null, null, null));
        Should.Throw<DomainException>(() => Feed().ChangeDividend(Cash, ExDate, null, null, null));
    }

    [Test]
    public void Change_OfADividend_WithARatio_Throws() =>
        Should.Throw<DomainException>(() =>
            Manual().Change(CorporateActionKind.Split, new CorporateActionRatio(2, 1), ExDate, null, null, null));

    [Test]
    public void AmendDividend_ALaterVersion_ReplacesTheTerms_AndAnOlderOneChangesNothing()
    {
        var action = Feed(2);
        var terms = DividendTerms.Create(1.10m, DividendTaxSource.Foreign, null, 15m, 26m);

        action.AmendDividend(2, terms, ExDate, null, null).ShouldBeFalse();
        action.Terms.ShouldBe(Plan);

        action.AmendDividend(3, terms, ExDate.AddDays(1), null, new DateOnly(2026, 12, 16)).ShouldBeTrue();
        action.Terms.ShouldBe(terms);
        (action.FeedVersion, action.ExDate).ShouldBe(((int?)3, ExDate.AddDays(1)));
    }

    [Test]
    public void Amend_OfADividend_WithARatio_Throws() =>
        Should.Throw<DomainException>(() => Feed().Amend(2, new CorporateActionRatio(2, 1), ExDate, null, null));

    [Test]
    public void AmendDividend_OfASplit_Throws() =>
        Should.Throw<DomainException>(() => CorporateAction
            .Announce(1, 10, "simulator", "w-CA1", 1, CorporateActionKind.Split, new CorporateActionRatio(2, 1), ExDate, null, null)
            .AmendDividend(2, Cash, ExDate, null, null));

    [Test]
    public void CreateDividend_StoresTheEntitledQuantityAndSourceAmounts_Pending()
    {
        var entitlement = CorporateActionEntitlement.CreateDividend(
            1, 5, 7, 100m, new DividendSourceAmounts(100m, 0m, 15m), true, 78.82352941m, At);

        entitlement.Status.ShouldBe(EntitlementStatus.Pending);
        entitlement.IsDividend.ShouldBeTrue();
        (entitlement.EntitledQuantity, entitlement.GrossAmount, entitlement.ImputationCredit, entitlement.ForeignTax)
            .ShouldBe(((decimal?)100m, (decimal?)100m, (decimal?)0m, (decimal?)15m));
        (entitlement.Reinvests, entitlement.ParticipatingQuantity).ShouldBe((true, (decimal?)78.82352941m));
        entitlement.ReinvestedQuantity.ShouldBeNull();
        entitlement.NetAmount.ShouldBeNull();
        entitlement.PostedAt.ShouldBeNull();
    }

    [Test]
    public void CreateDividend_WithAGrossOfZero_IsNotAppliedWithNothingToPay()
    {
        var entitlement = CorporateActionEntitlement.CreateDividend(
            1, 5, 7, 0.001m, new DividendSourceAmounts(0m, 0m, 0m), false, null, At);

        (entitlement.Status, entitlement.Reason, entitlement.PostedAt).ShouldBe(
            (EntitlementStatus.NotApplied, (EntitlementReason?)EntitlementReason.NothingToPay, (DateTimeOffset?)At));
        entitlement.EntitledQuantity.ShouldBe(0.001m);
    }

    [Test]
    public void CreateDividend_OutsideItsRules_Throws()
    {
        var amounts = new DividendSourceAmounts(72m, 28m, 0m);
        Should.Throw<DomainException>(() => CorporateActionEntitlement.CreateDividend(1, 5, 7, 0m, amounts, false, null, At));
        Should.Throw<DomainException>(() => CorporateActionEntitlement.CreateDividend(
            1, 5, 7, 100m, new DividendSourceAmounts(-1m, 0m, 0m), false, null, At));
        Should.Throw<DomainException>(() => CorporateActionEntitlement.CreateDividend(1, 5, 7, 100m, amounts, true, null, At));
        Should.Throw<DomainException>(() => CorporateActionEntitlement.CreateDividend(1, 5, 7, 100m, amounts, true, 0m, At));
        Should.Throw<DomainException>(() => CorporateActionEntitlement.CreateDividend(1, 5, 7, 100m, amounts, true, 101m, At));
        Should.Throw<DomainException>(() => CorporateActionEntitlement.CreateDividend(1, 5, 7, 100m, amounts, false, 50m, At));
    }

    [Test]
    public void MarkPosted_WithQuantities_OnADividendEntitlement_Throws()
    {
        var entitlement = CorporateActionEntitlement.CreateDividend(
            1, 5, 7, 100m, new DividendSourceAmounts(72m, 28m, 0m), false, null, At);

        Should.Throw<DomainException>(() => entitlement.MarkPosted(100m, 200m, 9, At));
    }

    [Test]
    public void Create_OfARatioKind_IsNotADividend() =>
        CorporateActionEntitlement.Create(1, 5, 7).IsDividend.ShouldBeFalse();

    [Test]
    public void IsStorable_ADividend_NeedsAnAmountAndATaxSource_AndNoRatio()
    {
        Storable(CorporateActionKind.Dividend, null, null, 0.72m, DividendTaxSource.NewZealand).ShouldBeTrue();
        Storable(CorporateActionKind.Dividend, null, null, null, DividendTaxSource.NewZealand).ShouldBeFalse();
        Storable(CorporateActionKind.Dividend, null, null, 0.72m, null).ShouldBeFalse();
        Storable(CorporateActionKind.Dividend, 2, 1, 0.72m, DividendTaxSource.NewZealand).ShouldBeFalse();
    }

    [Test]
    public void IsStorable_ARatioKind_StillNeedsBothRatioNumbers()
    {
        Storable(CorporateActionKind.Split, 2, 1, null, null).ShouldBeTrue();
        Storable(CorporateActionKind.Split, null, null, 0.72m, DividendTaxSource.NewZealand).ShouldBeFalse();
        Storable(null, 2, 1, null, null).ShouldBeTrue();
    }

    [Test]
    public void Receive_ADividend_StoresItsTermsAndNoRatio()
    {
        var row = CorporateActionAnnouncement.Receive(
            "simulator", "w-CA3-V1", "w-CA3", 1, PartyActionStatus.Announced, "ACME.SIM", "usd", CorporateActionKind.Dividend,
            null, null, ExDate, null, new DateOnly(2026, 12, 15), At,
            amountPerShare: 1.00m, taxSource: DividendTaxSource.Foreign, imputationCreditPerShare: 0m,
            foreignTaxRatePercent: 15m, reinvestmentPrice: 25m);

        (row.Kind, row.RatioNew, row.RatioHeld).ShouldBe(((CorporateActionKind?)CorporateActionKind.Dividend, (int?)null, (int?)null));
        (row.AmountPerShare, row.TaxSource, row.ImputationCreditPerShare, row.ForeignTaxRatePercent, row.ReinvestmentPrice)
            .ShouldBe(((decimal?)1.00m, (DividendTaxSource?)DividendTaxSource.Foreign, (decimal?)0m, (decimal?)15m, (decimal?)25m));
    }

    private static bool Storable(
        CorporateActionKind? kind, int? ratioNew, int? ratioHeld, decimal? amount, DividendTaxSource? taxSource) =>
        CorporateActionAnnouncement.IsStorable(
            "w-CA3-V1", "w-CA3", 1, PartyActionStatus.Announced, "ACME.SIM", "USD", ratioNew, ratioHeld, ExDate,
            kind, amount, taxSource);
}
