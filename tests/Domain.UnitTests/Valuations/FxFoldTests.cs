using MyWealthV2.Domain.Valuations;
using NUnit.Framework;
using Shouldly;
using static MyWealthV2.Domain.UnitTests.Valuations.WorkedExample;

namespace MyWealthV2.Domain.UnitTests.Valuations;

/// <summary>Valuation history R10, R11 and §1.1: the FX fold of one day.</summary>
public class FxFoldTests
{
    private const string Nzd = "NZD";
    private const string Usd = "USD";
    private static readonly IReadOnlyDictionary<string, StoredRate> NoRates = new Dictionary<string, StoredRate>();

    private static FoldedTotal Fold(DateOnly day, IReadOnlyDictionary<string, decimal> totals, IReadOnlyDictionary<string, StoredRate> rates) =>
        FxFold.Compute(totals, Nzd, 2, rates, day);

    [TestCase(4, 44_133.32, 1.7180, 12_885.43, 57_018.75)]
    [TestCase(5, 44_434.00, 1.7200, 12_900.43, 57_334.43)]
    [TestCase(6, 42_790.45, 1.7250, 12_937.93, 55_728.38)]
    [TestCase(7, 42_790.45, 1.7250, 12_937.93, 55_728.38)]
    [TestCase(8, 42_804.72, 1.7260, 12_945.43, 55_750.15)]
    [TestCase(9, 43_092.48, 1.7300, 12_975.43, 56_067.91)]
    [TestCase(10, 42_930.09, 1.7280, 12_960.43, 55_890.52)]
    [TestCase(11, 34_750.72, 1.7310, 12_982.93, 47_733.65)]
    public void WorkedExample_FoldsEachCurrencyOnce_AtTheRateOfTheDay(int day, decimal nzd, decimal rate, decimal usdValue, decimal total)
    {
        var row = new StoredRate(rate, Day(day), "Frankfurter");

        var folded = Fold(Day(day), new Dictionary<string, decimal> { [Usd] = 7_500.25m, [Nzd] = nzd }, new Dictionary<string, StoredRate> { [Usd] = row });

        folded.Currency.ShouldBe(Nzd);
        folded.Lines.ShouldBe([
            new FoldLine(Nzd, nzd, FoldLineStatus.Folded, null, nzd),
            new FoldLine(Usd, 7_500.25m, FoldLineStatus.Folded, row, usdValue)
        ]);
        folded.Total.ShouldBe(total);
        folded.Complete.ShouldBeTrue();
    }

    [Test]
    public void TheReportingCurrency_NeedsNoRate()
    {
        var folded = Fold(Day(2), new Dictionary<string, decimal> { [Nzd] = 41_550.00m }, NoRates);

        folded.Lines.ShouldBe([new FoldLine(Nzd, 41_550.00m, FoldLineStatus.Folded, null, 41_550.00m)]);
        folded.Total.ShouldBe(41_550.00m);
        folded.Complete.ShouldBeTrue();
    }

    [Test]
    public void ACurrencyWithNoRate_IsNotFolded_AndTheTotalLeavesItOut()
    {
        var folded = Fold(Day(11), new Dictionary<string, decimal> { [Nzd] = 8_500.00m, [Usd] = 800.00m }, NoRates);

        folded.Lines.ShouldBe([
            new FoldLine(Nzd, 8_500.00m, FoldLineStatus.Folded, null, 8_500.00m),
            new FoldLine(Usd, 800.00m, FoldLineStatus.NoRate, null, null)
        ]);
        folded.Total.ShouldBe(8_500.00m);
        folded.Complete.ShouldBeFalse();
    }

    [TestCase(4, FoldLineStatus.Folded)]
    [TestCase(5, FoldLineStatus.StaleRate)]
    public void ARate_IsUsedUpToFourDaysOld(int daysOld, FoldLineStatus status)
    {
        var row = new StoredRate(2m, Day(11).AddDays(-daysOld), "Frankfurter");

        var folded = Fold(Day(11), new Dictionary<string, decimal> { [Usd] = 100.00m }, new Dictionary<string, StoredRate> { [Usd] = row });

        var line = folded.Lines.ShouldHaveSingleItem();
        line.ShouldBe(new FoldLine(Usd, 100.00m, status, row, status == FoldLineStatus.Folded ? 200.00m : null));
        folded.Total.ShouldBe(status == FoldLineStatus.Folded ? 200.00m : 0m);
        folded.Complete.ShouldBe(status == FoldLineStatus.Folded);
    }

    [TestCase(1.25, 0.63)]
    [TestCase(-1.25, -0.63)]
    public void TheValue_IsRoundedAwayFromZero_ToTheReportingCurrency(decimal total, decimal value)
    {
        var row = new StoredRate(0.5m, Day(11), "Frankfurter");

        var folded = Fold(Day(11), new Dictionary<string, decimal> { [Usd] = total }, new Dictionary<string, StoredRate> { [Usd] = row });

        folded.Lines.ShouldHaveSingleItem().Value.ShouldBe(value);
        folded.Total.ShouldBe(value);
    }

    [Test]
    public void TheReportingCurrencyDecimalPlaces_AreUsed()
    {
        var row = new StoredRate(150.4m, Day(11), "Frankfurter");

        var folded = FxFold.Compute(new Dictionary<string, decimal> { [Usd] = 10.01m }, "JPY", 0, new Dictionary<string, StoredRate> { [Usd] = row }, Day(11));

        folded.Lines.ShouldHaveSingleItem().Value.ShouldBe(1_506m);
        folded.Total.ShouldBe(1_506m);
    }

    [Test]
    public void Lines_AreSortedByCurrency()
    {
        var rates = new Dictionary<string, StoredRate>
        {
            [Usd] = new(1.7m, Day(11), "Frankfurter"),
            ["AUD"] = new(1.1m, Day(11), "Frankfurter")
        };

        var folded = Fold(Day(11), new Dictionary<string, decimal> { [Usd] = 1m, [Nzd] = 1m, ["AUD"] = 1m }, rates);

        folded.Lines.Select(line => line.Currency).ShouldBe(["AUD", Nzd, Usd]);
        folded.Total.ShouldBe(3.80m);
    }

    [Test]
    public void NoTotals_FoldToZero_AndAreComplete()
    {
        var folded = Fold(Day(11), new Dictionary<string, decimal>(), NoRates);

        folded.Lines.ShouldBeEmpty();
        folded.Total.ShouldBe(0m);
        folded.Complete.ShouldBeTrue();
    }
}
