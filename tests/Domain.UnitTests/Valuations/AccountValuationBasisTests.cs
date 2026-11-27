using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Valuations;
using NUnit.Framework;
using Shouldly;
using static MyWealthV2.Domain.UnitTests.Valuations.WorkedExample;

namespace MyWealthV2.Domain.UnitTests.Valuations;

/// <summary>Account valuation R18, R19: the quantity and the price of a line are on the same side of a corporate
/// action.</summary>
public class AccountValuationBasisTests
{
    private const int Instrument = 7;

    private static readonly DateOnly Thursday = Day(5);

    [Test]
    public void WorkedExample_Thursday5_RestatesTheQuantityOfTheSplit()
    {
        var valuation = ValueOn(Thursday, BbbSplit);

        var bbb = valuation.Lines.Single(line => line.InstrumentId == Bbb);
        bbb.Status.ShouldBe(ValuationLineStatus.Valued);
        (bbb.Quantity, bbb.LedgerQuantity).ShouldBe((100m, 50m));
        bbb.Price.ShouldBe(new StoredPrice(92.50m, "USD", Thursday, "EODHD"));
        (bbb.MarketValue, bbb.Value).ShouldBe((9_250.00m, 15_910.00m));
        valuation.Cash.ShouldBe(16_356.60m);
        valuation.Lines.Select(line => (line.InstrumentId, line.Quantity, line.Value)).ShouldBe([
            (Aaa, 1_500m, 3_705.00m),
            (Bbb, 100m, 15_910.00m),
            (Ccc, 120m, 8_462.40m)
        ]);
        valuation.HoldingsValue.ShouldBe(28_077.40m);
        valuation.Total.ShouldBe(44_434.00m);
        valuation.Complete.ShouldBeTrue();
    }

    [Test]
    public void WorkedExample_TheOtherDays_KeepTheLedgerQuantity()
    {
        foreach (var day in new[] { 2, 3, 4, 6, 9 })
        {
            var bbb = ValueOn(Day(day), BbbSplit).Lines.Single(line => line.InstrumentId == Bbb);
            bbb.Quantity.ShouldBe(bbb.LedgerQuantity, $"March {day}");
        }
    }

    [Test]
    public void ARestatedFraction_IsRoundedDownToEightDecimals()
    {
        // 0.33333333 × 3 / 2 = 0.499999995 → 0.49999999.
        var line = Line(0.33333333m, PriceOn(Thursday), Change(CorporateActionKind.Split, 3, 2, Thursday, After())).Single();

        (line.Quantity, line.LedgerQuantity).ShouldBe((0.49999999m, 0.33333333m));
        line.MarketValue.ShouldBe(decimal.Round(0.49999999m * 10.00m, 2, MidpointRounding.AwayFromZero));
    }

    [Test]
    public void AConsolidation_UsesItsFactor() =>
        Line(1_000m, PriceOn(Thursday), Change(CorporateActionKind.Consolidation, 1, 10, Thursday, After()))
            .Single().Quantity.ShouldBe(100m);

    [Test]
    public void ABonus_UsesItsFactor() =>
        Line(100m, PriceOn(Thursday), Change(CorporateActionKind.Bonus, 1, 5, Thursday, After()))
            .Single().Quantity.ShouldBe(120m);

    [Test]
    public void APriceFromBeforeTheExDate_WithTheQuantityAfterIt_IsNotValued()
    {
        // "Apply now": the posting is booked on the ex-date and no price on the new basis has arrived.
        var price = PriceOn(Thursday.AddDays(-1));
        var line = Line(200m, price, Change(CorporateActionKind.Split, 2, 1, Thursday, Within())).Single();

        line.Status.ShouldBe(ValuationLineStatus.PriceBeforeCorporateAction);
        line.Price.ShouldBe(price);
        (line.MarketValue, line.Fx, line.Value).ShouldBe((null, null, null));
        (line.Quantity, line.LedgerQuantity).ShouldBe((200m, 200m));
    }

    [Test]
    public void BothBeforeTheCorporateAction_KeepTheQuantity()
    {
        var line = Line(100m, PriceOn(Thursday.AddDays(-1)), Change(CorporateActionKind.Split, 2, 1, Thursday, After())).Single();

        (line.Status, line.Quantity).ShouldBe((ValuationLineStatus.Valued, 100m));
    }

    [Test]
    public void BothAfterTheCorporateAction_KeepTheQuantity()
    {
        var line = Line(200m, PriceOn(Thursday), Change(CorporateActionKind.Split, 2, 1, Thursday, Within())).Single();

        (line.Status, line.Quantity).ShouldBe((ValuationLineStatus.Valued, 200m));
    }

    [Test]
    public void TwoPostings_AreTakenInTheOrderOfTheirExDates()
    {
        // 0.00000001 × 3/2 → 0.00000001, then × 2 → 0.00000002. The other order would give 0.00000003.
        var first = Change(CorporateActionKind.Split, 3, 2, Thursday.AddDays(-1), After());
        var second = Change(CorporateActionKind.Split, 2, 1, Thursday, After());

        Line(0.00000001m, PriceOn(Thursday), second, first).Single().Quantity.ShouldBe(0.00000002m);
    }

    [Test]
    public void TwoPostingsOnTheSameExDate_AreTakenInTheOrderOfTheirBookedAt()
    {
        var first = Change(CorporateActionKind.Split, 3, 2, Thursday, After());
        var second = Change(CorporateActionKind.Split, 2, 1, Thursday, After().AddHours(1));

        Line(0.00000001m, PriceOn(Thursday), second, first).Single().Quantity.ShouldBe(0.00000002m);
    }

    [Test]
    public void APostingOfAnotherInstrument_ChangesNothing()
    {
        var other = new BasisChange(Instrument + 1, Thursday, After(), CorporateActionKind.Split, 2, 1);

        var line = Line(100m, PriceOn(Thursday), other).Single();

        (line.Status, line.Quantity).ShouldBe((ValuationLineStatus.Valued, 100m));
    }

    [Test]
    public void AStalePrice_IsStalePrice_BeforeTheCorporateActionCheck()
    {
        var line = Line(200m, PriceOn(Thursday.AddDays(-5)), Change(CorporateActionKind.Split, 2, 1, Thursday, Within())).Single();

        line.Status.ShouldBe(ValuationLineStatus.StalePrice);
    }

    [Test]
    public void APriceBeforeTheCorporateAction_ComesBeforeNoRate()
    {
        var position = new AccountPosition(Thursday, 0m, [new PositionLine(Instrument, 200m)]);
        var prices = new Dictionary<int, StoredPrice>
        {
            [Instrument] = new(10.00m, "USD", Thursday.AddDays(-1), "EODHD")
        };

        var line = AccountValuation.Compute(
            position, "NZD", DecimalPlaces, prices, new Dictionary<string, StoredRate>(),
            [Change(CorporateActionKind.Split, 2, 1, Thursday, Within())]).Lines.Single();

        line.Status.ShouldBe(ValuationLineStatus.PriceBeforeCorporateAction);
    }

    [Test]
    public void ANegativeQuantity_IsNotRestated()
    {
        var line = Line(-5m, PriceOn(Thursday), Change(CorporateActionKind.Split, 2, 1, Thursday, After())).Single();

        (line.Status, line.Quantity).ShouldBe((ValuationLineStatus.NegativeQuantity, -5m));
    }

    [Test]
    public void APositionOfADay_IsCutAtTheEndOfTheDay() =>
        new AccountPosition(Thursday, 0m, []).CutAt.ShouldBe(LedgerDay.End(Thursday));

    [Test]
    public void APositionCutAtAnInstant_APostingBookedBeforeIt_IsOnTheNewBasis()
    {
        // Tax-year-events R22: the position is cut at 11:00 UTC; the posting was booked at 09:00, before the cut.
        var cutAt = LedgerDay.End(Thursday).AddHours(-13);
        var line = LineCutAt(cutAt, 200m, PriceOn(Thursday), Change(CorporateActionKind.Split, 2, 1, Thursday, cutAt.AddHours(-2))).Single();

        (line.Status, line.Quantity, line.LedgerQuantity).ShouldBe((ValuationLineStatus.Valued, 200m, 200m));
    }

    [Test]
    public void APositionCutAtAnInstant_APostingBookedAfterItTheSameDay_IsNotOnTheNewBasis()
    {
        // The posting was booked at 14:00, after the cut at 11:00 and before the day ended: the quantity is restated.
        var cutAt = LedgerDay.End(Thursday).AddHours(-13);
        var line = LineCutAt(cutAt, 100m, PriceOn(Thursday), Change(CorporateActionKind.Split, 2, 1, Thursday, cutAt.AddHours(3))).Single();

        (line.Status, line.Quantity, line.LedgerQuantity).ShouldBe((ValuationLineStatus.Valued, 200m, 100m));
        line.MarketValue.ShouldBe(2_000.00m);
    }

    [Test]
    public void APositionCutAtAnInstant_APostingBookedAfterIt_WithAPriceBeforeTheExDate_KeepsTheQuantity()
    {
        var cutAt = LedgerDay.End(Thursday).AddHours(-13);
        var line = LineCutAt(cutAt, 100m, PriceOn(Thursday.AddDays(-1)), Change(CorporateActionKind.Split, 2, 1, Thursday, cutAt.AddHours(3))).Single();

        (line.Status, line.Quantity).ShouldBe((ValuationLineStatus.Valued, 100m));
    }

    /// <summary>A posting booked after Thursday ended: the quantity of Thursday is on the old basis.</summary>
    private static DateTimeOffset After() => LedgerDay.End(Thursday).AddHours(12);

    /// <summary>A posting booked during Thursday: the quantity of Thursday is on the new basis.</summary>
    private static DateTimeOffset Within() => LedgerDay.End(Thursday).AddHours(-6);

    private static StoredPrice PriceOn(DateOnly date) => new(10.00m, "NZD", date, "EODHD");

    private static BasisChange Change(CorporateActionKind kind, int ratioNew, int ratioHeld, DateOnly exDate, DateTimeOffset bookedAt) =>
        new(Instrument, exDate, bookedAt, kind, ratioNew, ratioHeld);

    private static IReadOnlyList<ValuationLine> LineCutAt(
        DateTimeOffset cutAt, decimal ledgerQuantity, StoredPrice price, params BasisChange[] changes) =>
        AccountValuation.Compute(
            new AccountPosition(Thursday, 0m, [new PositionLine(Instrument, ledgerQuantity)], cutAt),
            "NZD",
            DecimalPlaces,
            new Dictionary<int, StoredPrice> { [Instrument] = price },
            new Dictionary<string, StoredRate>(),
            changes).Lines;

    private static IReadOnlyList<ValuationLine> Line(decimal ledgerQuantity, StoredPrice price, params BasisChange[] changes) =>
        AccountValuation.Compute(
            new AccountPosition(Thursday, 0m, [new PositionLine(Instrument, ledgerQuantity)]),
            "NZD",
            DecimalPlaces,
            new Dictionary<int, StoredPrice> { [Instrument] = price },
            new Dictionary<string, StoredRate>(),
            changes).Lines;
}
