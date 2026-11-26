using MyWealthV2.Application.Reports;
using MyWealthV2.Domain.Valuations;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Application.UnitTests.Reports;

/// <summary>Valuation-history R17–R19: the lines and subtotals of the Adviser Balances Report for a past date.</summary>
public class AdviserBalancesPastRowsTests
{
    private static readonly Guid CustomerA = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid CustomerB = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid A1 = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid A2 = Guid.Parse("00000000-0000-0000-0000-0000000000a2");
    private static readonly Guid B1 = Guid.Parse("00000000-0000-0000-0000-0000000000b1");
    private static readonly Guid Aaa = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Bbb = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid Ccc = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly DateOnly Wednesday = new(2026, 3, 11);

    /// <summary>§1.1: A1 at the end of Wednesday 11 March 2026.</summary>
    private static AdviserPastAccount WorkedExample() => Account(A1, "A1", "Custodial", "NZD", 14_356.60m,
        Holding(Aaa, "AAA", 1_500m, ValuationLineStatus.Valued, 2.61m, Wednesday, 3_915.00m, "NZD"),
        Holding(Ccc, "CCC", 120m, ValuationLineStatus.StalePrice, 41.30m, new DateOnly(2026, 3, 6), null, "USD"),
        Holding(Bbb, "BBB", 100m, ValuationLineStatus.Valued, 95.20m, Wednesday, 9_520.00m, "USD"));

    [Test]
    public void TheWorkedExample_ListsEveryLine_AndLeavesTheHoldingNotValuedOutOfTheSubtotals()
    {
        var selection = AdviserBalancesPastRows.Select([WorkedExample()]);

        selection.Lines.Select(Describe).ShouldBe([
            "cash A1 NZD 14356.60",
            "holding A1 AAA 1500 Valued 3915.00 NZD",
            "holding A1 BBB 100 Valued 9520.00 USD",
            "holding A1 CCC 120 StalePrice - USD"
        ]);
        var ccc = selection.Lines.OfType<AdviserPastHoldingLine>().Single(line => line.Symbol == "CCC");
        (ccc.PriceAmount, ccc.PriceDate).ShouldBe((41.30m, new DateOnly(2026, 3, 6)));
        selection.Subtotals.ShouldBe([
            new AdviserBalanceSubtotal("NZD", 14_356.60m, 3_915.00m),
            new AdviserBalanceSubtotal("USD", 0m, 9_520.00m)
        ]);
        selection.ModeSubtotals.ShouldHaveSingleItem().CustodyMode.ShouldBe("Custodial");
        selection.ModeSubtotals[0].Subtotals.ShouldBe(selection.Subtotals);
        (selection.HoldingsNotValued, selection.AccountsNotValuedYet).ShouldBe((1, 0));
    }

    [TestCase(ValuationLineStatus.NoPrice)]
    [TestCase(ValuationLineStatus.StalePrice)]
    [TestCase(ValuationLineStatus.PriceBeforeCorporateAction)]
    [TestCase(ValuationLineStatus.NegativeQuantity)]
    public void ALineWithNoMarketValue_IsListed_AndLeftOutOfTheSubtotals(ValuationLineStatus status)
    {
        var selection = AdviserBalancesPastRows.Select([
            Account(A1, "A1", "External", "NZD", 10.00m, Holding(Aaa, "AAA", status == ValuationLineStatus.NegativeQuantity ? -5m : 5m, status, null, null, null, "USD"))
        ]);

        selection.Lines.OfType<AdviserPastHoldingLine>().ShouldHaveSingleItem().Status.ShouldBe(status);
        selection.Subtotals.ShouldBe([new AdviserBalanceSubtotal("NZD", 10.00m, 0m)]);
        selection.HoldingsNotValued.ShouldBe(1);
    }

    [TestCase(ValuationLineStatus.NoRate)]
    [TestCase(ValuationLineStatus.StaleRate)]
    public void ALineWithNoRate_HasItsMarketValueInTheQuoteCurrency_AndCounts(ValuationLineStatus status)
    {
        var selection = AdviserBalancesPastRows.Select([
            Account(A1, "A1", "Custodial", "NZD", 0m, Holding(Bbb, "BBB", 10m, status, 95.20m, Wednesday, 952.00m, "USD"))
        ]);

        selection.Subtotals.ShouldBe([new AdviserBalanceSubtotal("USD", 0m, 952.00m)]);
        selection.HoldingsNotValued.ShouldBe(0);
    }

    [Test]
    public void ACustodialAccountWithNoStoredValuation_IsOneLineNotValuedYet()
    {
        var selection = AdviserBalancesPastRows.Select([
            WorkedExample(),
            Account(B1, "B1", "Custodial", "USD", 0m) with { CustomerId = CustomerB, CustomerName = "Bea", NotValuedYet = true }
        ]);

        selection.Lines.Select(Describe).LastOrDefault().ShouldBe("not valued B1");
        selection.Subtotals.Select(subtotal => subtotal.Currency).ShouldBe(["NZD", "USD"]);
        selection.Subtotals[1].ShouldBe(new AdviserBalanceSubtotal("USD", 0m, 9_520.00m));
        (selection.HoldingsNotValued, selection.AccountsNotValuedYet).ShouldBe((1, 1));
    }

    [Test]
    public void AnAccountWithNoCashAndNoHolding_IsLeftOut_AndOneWithHoldingsOnlyHasNoCashLine()
    {
        var selection = AdviserBalancesPastRows.Select([
            Account(A1, "A1", "Custodial", "NZD", 0m),
            Account(A2, "A2", "External", "NZD", 0m, Holding(Aaa, "AAA", 3m, ValuationLineStatus.Valued, 2m, Wednesday, 6.00m, "NZD"))
        ]);

        selection.Lines.Select(Describe).ShouldBe(["holding A2 AAA 3 Valued 6.00 NZD"]);
        selection.ModeSubtotals.Select(block => block.CustodyMode).ShouldBe(["External"]);
    }

    [Test]
    public void Lines_AreOrderedByCustomerThenAccount_CashFirst()
    {
        var selection = AdviserBalancesPastRows.Select([
            Account(B1, "B1", "Custodial", "USD", 1m) with { CustomerId = CustomerB, CustomerName = "Bea" },
            Account(A2, "A2", "External", "NZD", 2m),
            WorkedExample()
        ]);

        selection.Lines.Select(line => line.AccountName).Distinct().ShouldBe(["A1", "A2", "B1"]);
    }

    [TestCase(ValuationLineStatus.Valued, null)]
    [TestCase(ValuationLineStatus.NoRate, null)]
    [TestCase(ValuationLineStatus.StaleRate, null)]
    [TestCase(ValuationLineStatus.NoPrice, "No price")]
    [TestCase(ValuationLineStatus.StalePrice, "Price too old")]
    [TestCase(ValuationLineStatus.PriceBeforeCorporateAction, "Price from before a corporate action")]
    [TestCase(ValuationLineStatus.NegativeQuantity, "Quantity below 0")]
    public void StatusLabel_NamesALineThatIsNotValued(ValuationLineStatus status, string? label) =>
        AdviserBalancesPastRows.StatusLabel(status).ShouldBe(label);

    private static AdviserPastAccount Account(
        Guid id, string name, string custodyMode, string currency, decimal cash, params AdviserPastHolding[] holdings) =>
        new(CustomerA, "Anna", id, name, "Brokerage", custodyMode, currency, false, cash, holdings);

    private static AdviserPastHolding Holding(
        Guid id, string symbol, decimal quantity, ValuationLineStatus status, decimal? price, DateOnly? priceDate,
        decimal? marketValue, string quoteCurrency) =>
        new(id, symbol, quantity, quantity, status, price, priceDate, marketValue, quoteCurrency);

    private static string Describe(AdviserBalanceLine line) => line switch
    {
        AdviserCashLine cash => $"cash {cash.AccountName} {cash.Currency} {cash.Balance:0.00}",
        AdviserPastHoldingLine holding =>
            $"holding {holding.AccountName} {holding.Symbol} {holding.Quantity:0.##} {holding.Status} {(holding.MarketValue is { } value ? value.ToString("0.00") : "-")} {holding.MarketCurrency}",
        AdviserNotValuedLine notValued => $"not valued {notValued.AccountName}",
        _ => line.GetType().Name
    };
}
