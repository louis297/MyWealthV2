using MyWealthV2.Domain.CorporateActions;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.CorporateActions;

/// <summary>Corporate-actions R3 and R28: the ratio, what each kind requires, and the quantity after.</summary>
public class CorporateActionRatioTests
{
    [TestCase(CorporateActionKind.Split, 2, 1, true)]
    [TestCase(CorporateActionKind.Split, 3, 2, true)]
    [TestCase(CorporateActionKind.Split, 1, 1, false)]
    [TestCase(CorporateActionKind.Split, 1, 2, false)]
    [TestCase(CorporateActionKind.Consolidation, 1, 10, true)]
    [TestCase(CorporateActionKind.Consolidation, 1, 1, false)]
    [TestCase(CorporateActionKind.Consolidation, 2, 1, false)]
    [TestCase(CorporateActionKind.Bonus, 1, 5, true)]
    [TestCase(CorporateActionKind.Bonus, 5, 1, true)]
    [TestCase(CorporateActionKind.Bonus, 1, 1, true)]
    public void IsValidFor_HoldsWhatEachKindRequires(CorporateActionKind kind, int ratioNew, int ratioHeld, bool valid) =>
        new CorporateActionRatio(ratioNew, ratioHeld).IsValidFor(kind).ShouldBe(valid);

    [TestCase(0, 1)]
    [TestCase(2, 0)]
    [TestCase(-2, 1)]
    [TestCase(10_001, 1)]
    [TestCase(10_000, 10_001)]
    public void IsValidFor_ANumberOutside1To10000_IsNotValidForAnyKind(int ratioNew, int ratioHeld)
    {
        var ratio = new CorporateActionRatio(ratioNew, ratioHeld);

        foreach (var kind in Enum.GetValues<CorporateActionKind>())
        {
            ratio.IsValidFor(kind).ShouldBeFalse();
        }
    }

    [Test]
    public void IsValidFor_TheBounds_AreValid()
    {
        new CorporateActionRatio(10_000, 1).IsValidFor(CorporateActionKind.Split).ShouldBeTrue();
        new CorporateActionRatio(1, 10_000).IsValidFor(CorporateActionKind.Consolidation).ShouldBeTrue();
        new CorporateActionRatio(10_000, 10_000).IsValidFor(CorporateActionKind.Bonus).ShouldBeTrue();
    }

    [TestCase(CorporateActionKind.Split, 2, 1, "100", "200")]
    [TestCase(CorporateActionKind.Split, 3, 2, "7", "10.5")]
    [TestCase(CorporateActionKind.Consolidation, 1, 10, "105", "10.5")]
    [TestCase(CorporateActionKind.Bonus, 1, 5, "12.34567891", "14.81481469")]
    [TestCase(CorporateActionKind.Bonus, 1, 4, "100", "125")]
    [TestCase(CorporateActionKind.Consolidation, 1, 3, "1", "0.33333333")]
    [TestCase(CorporateActionKind.Split, 4, 3, "0.00000002", "0.00000002")]
    public void Apply_GivesTheQuantityAfter_RoundedDownTo8Decimals(
        CorporateActionKind kind, int ratioNew, int ratioHeld, string before, string after) =>
        new CorporateActionRatio(ratioNew, ratioHeld).Apply(kind, decimal.Parse(before, System.Globalization.CultureInfo.InvariantCulture))
            .ShouldBe(decimal.Parse(after, System.Globalization.CultureInfo.InvariantCulture));

    [Test]
    public void Apply_ATinyHoldingConsolidated_GivesZero() =>
        new CorporateActionRatio(1, 10).Apply(CorporateActionKind.Consolidation, 0.00000009m).ShouldBe(0m);

    [Test]
    public void Apply_ARatioThatChangesNothingAt8Decimals_GivesTheSameQuantity() =>
        new CorporateActionRatio(10_000, 9_999).Apply(CorporateActionKind.Split, 0.00000001m).ShouldBe(0.00000001m);

    [Test]
    public void Apply_ARatioThatDoesNotFitTheKind_Throws() =>
        Should.Throw<DomainException>(() => new CorporateActionRatio(1, 2).Apply(CorporateActionKind.Split, 100m));

    [Test]
    public void Apply_ANegativeQuantity_Throws() =>
        Should.Throw<DomainException>(() => new CorporateActionRatio(2, 1).Apply(CorporateActionKind.Split, -1m));

    [TestCase(2, 1, "2 for 1")]
    [TestCase(1, 10, "1 for 10")]
    [TestCase(4, 2, "4 for 2")]
    public void Text_ReadsNewForHeld_AsGiven(int ratioNew, int ratioHeld, string text) =>
        new CorporateActionRatio(ratioNew, ratioHeld).Text.ShouldBe(text);
}
