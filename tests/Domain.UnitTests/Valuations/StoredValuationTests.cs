using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using MyWealthV2.Domain.Valuations;
using NUnit.Framework;
using Shouldly;
using static MyWealthV2.Domain.UnitTests.Valuations.WorkedExample;

namespace MyWealthV2.Domain.UnitTests.Valuations;

/// <summary>Valuation-run R8, R9, R11: a stored valuation copies the valuation as it was worked out.</summary>
public class StoredValuationTests
{
    private static readonly DateTimeOffset At = new(2026, 3, 7, 13, 0, 2, TimeSpan.Zero);

    [Test]
    public void From_Thursday5_CopiesEveryFigure_AndTheRestatedQuantity()
    {
        var valuation = ValueOn(Day(5), BbbSplit);

        var stored = StoredValuation.From(4, 11, valuation, 9, At);

        (stored.TenantId, stored.AccountId, stored.Date, stored.Currency, stored.ValuationRunId, stored.ValuedAt)
            .ShouldBe((4, 11, Day(5), "NZD", 9, At));
        (stored.Cash, stored.HoldingsValue, stored.Total, stored.Complete, stored.UnvaluedCount)
            .ShouldBe((valuation.Cash, valuation.HoldingsValue, valuation.Total, true, 0));
        stored.Total.ShouldBe(44_434.00m);
        stored.Lines.Count.ShouldBe(valuation.Lines.Count);
        foreach (var line in valuation.Lines)
        {
            var copy = stored.Lines.Single(row => row.InstrumentId == line.InstrumentId);
            copy.TenantId.ShouldBe(4);
            (copy.Quantity, copy.LedgerQuantity, copy.Status, copy.MarketValue, copy.Value)
                .ShouldBe((line.Quantity, line.LedgerQuantity, line.Status, line.MarketValue, line.Value));
            (copy.PriceAmount, copy.PriceCurrency, copy.PriceDate, copy.PriceSource)
                .ShouldBe((line.Price!.Amount, line.Price.Currency, (DateOnly?)line.Price.AsOfDate, line.Price.Source));
        }

        var bbb = stored.Lines.Single(row => row.InstrumentId == Bbb);
        (bbb.Quantity, bbb.LedgerQuantity).ShouldBe((100m, 50m));
        (bbb.FxRate, bbb.FxDate, bbb.FxSource).ShouldBe((1.7200m, (DateOnly?)Day(5), "Frankfurter"));
        var aaa = stored.Lines.Single(row => row.InstrumentId == Aaa);
        (aaa.FxRate, aaa.FxDate, aaa.FxSource).ShouldBe(((decimal?)null, (DateOnly?)null, (string?)null));
    }

    [Test]
    public void From_Wednesday11_KeepsTheIncompleteMark_AndTheStaleLineWithItsPriceRow()
    {
        var valuation = ValueOn(Day(11), BbbSplit);

        var stored = StoredValuation.From(4, 11, valuation, 9, At);

        (stored.Total, stored.Complete, stored.UnvaluedCount).ShouldBe((34_750.72m, false, 1));
        var ccc = stored.Lines.Single(row => row.InstrumentId == Ccc);
        ccc.ShouldSatisfyAllConditions(
            line => line.Status.ShouldBe(ValuationLineStatus.StalePrice),
            line => line.Quantity.ShouldBe(120m),
            line => line.PriceAmount.ShouldBe(41.30m),
            line => line.PriceCurrency.ShouldBe("USD"),
            line => line.PriceDate.ShouldBe(Day(6)),
            line => line.PriceSource.ShouldBe("EODHD"),
            line => line.MarketValue.ShouldBeNull(),
            line => line.FxRate.ShouldBeNull(),
            line => line.Value.ShouldBeNull());
    }

    [Test]
    public void From_AnEmptyDay_IsCompleteWithNoLine()
    {
        var valuation = new AccountValuation(Day(1), "NZD", 0m, [], 0m, 0m, true, 0);

        var stored = StoredValuation.From(4, 11, valuation, 9, At);

        (stored.Cash, stored.HoldingsValue, stored.Total, stored.Complete, stored.UnvaluedCount)
            .ShouldBe((0m, 0m, 0m, true, 0));
        stored.Lines.ShouldBeEmpty();
    }

    [Test]
    public void From_WithoutAValuation_IsRefused()
    {
        Should.Throw<ArgumentNullException>(() => StoredValuation.From(4, 11, null!, 9, At));
    }

    [Test]
    public void From_ATotalThatIsNotTheCashPlusTheValues_IsRefused()
    {
        var valuation = ValueOn(Day(5), BbbSplit) with { Total = 1m };

        Should.Throw<DomainException>(() => StoredValuation.From(4, 11, valuation, 9, At));
    }
}
