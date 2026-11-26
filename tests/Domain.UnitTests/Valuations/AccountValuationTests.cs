using MyWealthV2.Domain.Valuations;
using NUnit.Framework;
using Shouldly;
using static MyWealthV2.Domain.UnitTests.Valuations.WorkedExample;

namespace MyWealthV2.Domain.UnitTests.Valuations;

/// <summary>Account valuation R9, R10, R15, R16: valued lines, amounts and totals, checked against §1.1.</summary>
public class AccountValuationTests
{
    /// <summary>The days of §1.1 on which every line is valued and R18 changes nothing: cash, then
    /// (market value, value) of AAA, BBB and CCC, then the total.</summary>
    private static IEnumerable<TestCaseData> ValuedDays()
    {
        yield return Case(2, 10_000.00m, (2_480.00m, 2_480.00m), (9_000.00m, 15_390.00m), (8_000.00m, 13_680.00m), 41_550.00m);
        yield return Case(3, 8_735.00m, (3_750.00m, 3_750.00m), (9_100.00m, 15_606.50m), (8_100.00m, 13_891.50m), 41_983.00m);
        yield return Case(4, 10_735.00m, (3_780.00m, 3_780.00m), (9_200.00m, 15_805.60m), (8_040.00m, 13_812.72m), 44_133.32m);
        yield return Case(6, 14_356.60m, (3_825.00m, 3_825.00m), (9_310.00m, 16_059.75m), (4_956.00m, 8_549.10m), 42_790.45m);
        yield return Case(7, 14_356.60m, (3_825.00m, 3_825.00m), (9_310.00m, 16_059.75m), (4_956.00m, 8_549.10m), 42_790.45m);
        yield return Case(8, 14_356.60m, (3_825.00m, 3_825.00m), (9_310.00m, 16_069.06m), (4_956.00m, 8_554.06m), 42_804.72m);
        yield return Case(9, 14_356.60m, (3_900.00m, 3_900.00m), (9_400.00m, 16_262.00m), (4_956.00m, 8_573.88m), 43_092.48m);
        yield return Case(10, 14_356.60m, (3_870.00m, 3_870.00m), (9_340.00m, 16_139.52m), (4_956.00m, 8_563.97m), 42_930.09m);
    }

    private static TestCaseData Case(
        int day,
        decimal cash,
        (decimal Market, decimal Value) aaa,
        (decimal Market, decimal Value) bbb,
        (decimal Market, decimal Value) ccc,
        decimal total) =>
        new TestCaseData(day, cash, aaa, bbb, ccc, total).SetName($"WorkedExample_March{day}");

    [TestCaseSource(nameof(ValuedDays))]
    public void WorkedExample_EveryLineAndTheTotals(
        int day,
        decimal cash,
        (decimal Market, decimal Value) aaa,
        (decimal Market, decimal Value) bbb,
        (decimal Market, decimal Value) ccc,
        decimal total)
    {
        var valuation = ValueOn(Day(day), BbbSplit);

        valuation.Day.ShouldBe(Day(day));
        valuation.Currency.ShouldBe("NZD");
        valuation.Cash.ShouldBe(cash);
        valuation.Lines.Select(line => (line.InstrumentId, line.Status, line.MarketValue, line.Value)).ShouldBe([
            (Aaa, ValuationLineStatus.Valued, aaa.Market, aaa.Value),
            (Bbb, ValuationLineStatus.Valued, bbb.Market, bbb.Value),
            (Ccc, ValuationLineStatus.Valued, ccc.Market, ccc.Value)
        ]);
        valuation.HoldingsValue.ShouldBe(aaa.Value + bbb.Value + ccc.Value);
        valuation.Total.ShouldBe(total);
        valuation.Complete.ShouldBeTrue();
        valuation.UnvaluedCount.ShouldBe(0);
    }

    [Test]
    public void ALineInTheAccountCurrency_HasNoRate_AndItsValueIsTheMarketValue()
    {
        var aaa = ValueOn(Day(3), BbbSplit).Lines.Single(line => line.InstrumentId == Aaa);

        aaa.Quantity.ShouldBe(1_500m);
        aaa.LedgerQuantity.ShouldBe(1_500m);
        aaa.Price.ShouldBe(new StoredPrice(2.50m, "NZD", Day(3), "EODHD"));
        aaa.Fx.ShouldBeNull();
        aaa.Value.ShouldBe(aaa.MarketValue);
    }

    [Test]
    public void ALineInAnotherCurrency_ShowsThePriceAndTheRateRowsItUsed()
    {
        var ccc = ValueOn(Day(9), BbbSplit).Lines.Single(line => line.InstrumentId == Ccc);

        ccc.Quantity.ShouldBe(120m);
        ccc.Price.ShouldBe(new StoredPrice(41.30m, "USD", Day(6), "EODHD"));
        ccc.Fx.ShouldBe(new StoredRate(1.7300m, Day(9), "Frankfurter"));
    }

    [Test]
    public void TheWeekend_UsesFridaysPrice_AndEachDaysRate()
    {
        var saturday = ValueOn(Day(7), BbbSplit).Lines.Single(line => line.InstrumentId == Bbb);
        var sunday = ValueOn(Day(8), BbbSplit).Lines.Single(line => line.InstrumentId == Bbb);

        (saturday.Price!.AsOfDate, sunday.Price!.AsOfDate).ShouldBe((Day(6), Day(6)));
        (saturday.Fx!.AsOfDate, sunday.Fx!.AsOfDate).ShouldBe((Day(7), Day(8)));
        sunday.Value.ShouldNotBe(saturday.Value);
    }

    [Test]
    public void Rounding_TheMarketValueToTheQuoteCurrency_ThenTheValueFromTheRoundedMarketValue()
    {
        // R15: 0.33333333 × 92.50 = 30.83333303 → 30.83; 30.83 × 1.7200 = 53.0276 → 53.03.
        var valuation = Single(0.33333333m, new StoredPrice(92.50m, "USD", Day(5), "EODHD"), "NZD", 1.7200m);

        var line = valuation.Lines.Single();
        line.MarketValue.ShouldBe(30.83m);
        line.Value.ShouldBe(53.03m);
        valuation.Total.ShouldBe(53.03m);
    }

    [Test]
    public void Rounding_IsAwayFromZero()
    {
        var line = Single(1m, new StoredPrice(0.125m, "NZD", Day(5), "EODHD"), "NZD", null).Lines.Single();

        line.MarketValue.ShouldBe(0.13m);
        line.Value.ShouldBe(0.13m);
    }

    [Test]
    public void Rounding_ToACurrencyWithNoDecimalPlaces()
    {
        // 3 × 10.00 USD = 30.00 USD; 30.00 × 150.123 = 4503.69 → 4504 JPY.
        var line = Single(3m, new StoredPrice(10.00m, "USD", Day(5), "EODHD"), "JPY", 150.123m).Lines.Single();

        line.MarketValue.ShouldBe(30.00m);
        line.Value.ShouldBe(4_504m);
    }

    [Test]
    public void TheCash_IsNotRounded()
    {
        var position = new AccountPosition(Day(5), 12.3456m, []);

        var valuation = AccountValuation.Compute(position, "NZD", DecimalPlaces, new Dictionary<int, StoredPrice>(), new Dictionary<string, StoredRate>(), []);

        (valuation.Cash, valuation.Total).ShouldBe((12.3456m, 12.3456m));
    }

    [Test]
    public void NoLine_IsComplete()
    {
        var valuation = AccountValuation.Compute(
            new AccountPosition(Day(1), 0m, []), "NZD", DecimalPlaces, new Dictionary<int, StoredPrice>(), new Dictionary<string, StoredRate>(), []);

        valuation.Lines.ShouldBeEmpty();
        (valuation.HoldingsValue, valuation.Total, valuation.Complete, valuation.UnvaluedCount).ShouldBe((0m, 0m, true, 0));
    }

    private static AccountValuation Single(decimal quantity, StoredPrice price, string accountCurrency, decimal? rate)
    {
        var rates = new Dictionary<string, StoredRate>();
        if (rate is { } value)
        {
            rates[price.Currency] = new StoredRate(value, Day(5), "Frankfurter");
        }

        return AccountValuation.Compute(
            new AccountPosition(Day(5), 0m, [new PositionLine(7, quantity)]),
            accountCurrency,
            DecimalPlaces,
            new Dictionary<int, StoredPrice> { [7] = price },
            rates,
            []);
    }
}
