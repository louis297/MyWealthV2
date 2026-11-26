using MyWealthV2.Domain.Valuations;
using NUnit.Framework;
using Shouldly;
using static MyWealthV2.Domain.UnitTests.Valuations.WorkedExample;

namespace MyWealthV2.Domain.UnitTests.Valuations;

/// <summary>Account valuation R11, R14, R16: the age limit and the status of a line that is not valued.</summary>
public class AccountValuationStatusTests
{
    private static readonly DateOnly Today = Day(11);

    [TestCase(0, true)]
    [TestCase(4, true)]
    [TestCase(5, false)]
    [TestCase(30, false)]
    public void IsFresh_UpToFourDaysOld(int daysOld, bool fresh) =>
        ValuationRules.IsFresh(Today.AddDays(-daysOld), Today).ShouldBe(fresh);

    [Test]
    public void WorkedExample_Wednesday11_CccIsStale_AndTheValuationIsIncomplete()
    {
        var valuation = ValueOn(Day(11), BbbSplit);

        var ccc = valuation.Lines.Single(line => line.InstrumentId == Ccc);
        ccc.Status.ShouldBe(ValuationLineStatus.StalePrice);
        ccc.Quantity.ShouldBe(120m);
        ccc.Price.ShouldBe(new StoredPrice(41.30m, "USD", Day(6), "EODHD"));
        (ccc.MarketValue, ccc.Fx, ccc.Value).ShouldBe((null, null, null));
        valuation.Lines.Where(line => line.InstrumentId != Ccc).Select(line => (line.Status, line.Value)).ShouldBe([
            (ValuationLineStatus.Valued, 3_915.00m),
            (ValuationLineStatus.Valued, 16_479.12m)
        ]);
        valuation.HoldingsValue.ShouldBe(20_394.12m);
        valuation.Total.ShouldBe(34_750.72m);
        valuation.Complete.ShouldBeFalse();
        valuation.UnvaluedCount.ShouldBe(1);
    }

    [Test]
    public void APriceFourDaysOld_IsValued()
    {
        var line = Usd(5m, PriceOn(-4), RateOn(0)).Lines.Single();

        line.Status.ShouldBe(ValuationLineStatus.Valued);
        line.Value.ShouldBe(Round2(5m * 10.00m * 1.5m));
    }

    [Test]
    public void APriceFiveDaysOld_IsStalePrice()
    {
        var valuation = Usd(5m, PriceOn(-5), RateOn(0));

        var line = valuation.Lines.Single();
        line.Status.ShouldBe(ValuationLineStatus.StalePrice);
        line.Price.ShouldBe(PriceOn(-5));
        (line.MarketValue, line.Fx, line.Value).ShouldBe((null, null, null));
        (valuation.Total, valuation.Complete, valuation.UnvaluedCount).ShouldBe((100m, false, 1));
    }

    [Test]
    public void NoPriceRow_IsNoPrice()
    {
        var valuation = Usd(5m, null, RateOn(0));

        var line = valuation.Lines.Single();
        line.Status.ShouldBe(ValuationLineStatus.NoPrice);
        (line.Price, line.MarketValue, line.Fx, line.Value).ShouldBe((null, null, null, null));
        (line.Quantity, line.LedgerQuantity).ShouldBe((5m, 5m));
        (valuation.HoldingsValue, valuation.Total, valuation.Complete).ShouldBe((0m, 100m, false));
    }

    [Test]
    public void NoRateRow_IsNoRate_WithTheMarketValue()
    {
        var valuation = Usd(5m, PriceOn(0), null);

        var line = valuation.Lines.Single();
        line.Status.ShouldBe(ValuationLineStatus.NoRate);
        line.Price.ShouldBe(PriceOn(0));
        line.MarketValue.ShouldBe(50.00m);
        (line.Fx, line.Value).ShouldBe((null, null));
        (valuation.Total, valuation.Complete, valuation.UnvaluedCount).ShouldBe((100m, false, 1));
    }

    [Test]
    public void ARateFourDaysOld_IsValued() =>
        Usd(5m, PriceOn(0), RateOn(-4)).Lines.Single().Status.ShouldBe(ValuationLineStatus.Valued);

    [Test]
    public void ARateFiveDaysOld_IsStaleRate_WithTheRateRow()
    {
        var line = Usd(5m, PriceOn(0), RateOn(-5)).Lines.Single();

        line.Status.ShouldBe(ValuationLineStatus.StaleRate);
        line.MarketValue.ShouldBe(50.00m);
        line.Fx.ShouldBe(RateOn(-5));
        line.Value.ShouldBeNull();
    }

    [Test]
    public void ANegativeQuantity_IsNegativeQuantity_BeforeAnyPrice()
    {
        var line = Usd(-5m, PriceOn(0), RateOn(0)).Lines.Single();

        line.Status.ShouldBe(ValuationLineStatus.NegativeQuantity);
        (line.Price, line.MarketValue, line.Fx, line.Value).ShouldBe((null, null, null, null));
        line.LedgerQuantity.ShouldBe(-5m);
    }

    [Test]
    public void ANegativeQuantityWithNoPrice_IsNegativeQuantity() =>
        Usd(-5m, null, RateOn(0)).Lines.Single().Status.ShouldBe(ValuationLineStatus.NegativeQuantity);

    [Test]
    public void AStalePriceWithNoRate_IsStalePrice() =>
        Usd(5m, PriceOn(-5), null).Lines.Single().Status.ShouldBe(ValuationLineStatus.StalePrice);

    [Test]
    public void AStalePriceWithAStaleRate_IsStalePrice() =>
        Usd(5m, PriceOn(-5), RateOn(-5)).Lines.Single().Status.ShouldBe(ValuationLineStatus.StalePrice);

    [Test]
    public void AStalePriceInTheAccountCurrency_IsStalePrice()
    {
        var position = new AccountPosition(Today, 0m, [new PositionLine(7, 5m)]);
        var price = new StoredPrice(10.00m, "NZD", Today.AddDays(-5), "EODHD");

        var line = AccountValuation.Compute(
            position, "NZD", DecimalPlaces, new Dictionary<int, StoredPrice> { [7] = price },
            new Dictionary<string, StoredRate>(), []).Lines.Single();

        line.Status.ShouldBe(ValuationLineStatus.StalePrice);
    }

    [Test]
    public void TheLinesThatAreValued_StillAddUp_WhenAnotherIsNot()
    {
        var position = new AccountPosition(Today, 10m, [new PositionLine(7, 5m), new PositionLine(8, 2m)]);
        var prices = new Dictionary<int, StoredPrice>
        {
            [7] = new(10.00m, "NZD", Today, "EODHD"),
            [8] = new(3.00m, "USD", Today, "EODHD")
        };

        var valuation = AccountValuation.Compute(position, "NZD", DecimalPlaces, prices, new Dictionary<string, StoredRate>(), []);

        valuation.Lines.Select(line => line.Status).ShouldBe([ValuationLineStatus.Valued, ValuationLineStatus.NoRate]);
        (valuation.HoldingsValue, valuation.Total, valuation.Complete, valuation.UnvaluedCount).ShouldBe((50m, 60m, false, 1));
    }

    private static StoredPrice PriceOn(int days) => new(10.00m, "USD", Today.AddDays(days), "EODHD");

    private static StoredRate RateOn(int days) => new(1.5m, Today.AddDays(days), "Frankfurter");

    private static decimal Round2(decimal amount) => decimal.Round(amount, 2, MidpointRounding.AwayFromZero);

    /// <summary>One USD line on an NZD account holding cash 100 on <see cref="Today"/>.</summary>
    private static AccountValuation Usd(decimal quantity, StoredPrice? price, StoredRate? rate)
    {
        var prices = new Dictionary<int, StoredPrice>();
        if (price is not null)
        {
            prices[7] = price;
        }

        var rates = new Dictionary<string, StoredRate>();
        if (rate is not null)
        {
            rates["USD"] = rate;
        }

        return AccountValuation.Compute(
            new AccountPosition(Today, 100m, [new PositionLine(7, quantity)]), "NZD", DecimalPlaces, prices, rates, []);
    }
}
