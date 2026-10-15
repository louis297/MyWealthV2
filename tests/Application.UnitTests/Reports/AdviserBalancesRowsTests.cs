using MyWealthV2.Application.Reports;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Application.UnitTests.Reports;

public class AdviserBalancesRowsTests
{
    private static readonly Guid CustomerId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    [Test]
    public void Select_OmitsZeroCashAndKeepsNegative()
    {
        var empty = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var overdrawn = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var everyday = Guid.Parse("00000000-0000-0000-0000-000000000003");

        var selection = AdviserBalancesRows.Select(
            [
                Account(empty, "Empty", 0m),
                Account(overdrawn, "Overdrawn", -2.5m),
                Account(everyday, "Everyday", 10m)
            ],
            includeClosedAccounts: false);

        var cash = selection.Lines.ShouldBeAssignableTo<IReadOnlyList<AdviserBalanceLine>>()!;
        cash.Select(line => line.AccountId).ShouldBe([overdrawn, everyday]);
        var negative = cash[0].ShouldBeOfType<AdviserCashLine>();
        negative.Balance.ShouldBe(-2.5m);
        negative.Currency.ShouldBe("NZD");
        negative.AccountType.ShouldBe("Bank");
        negative.CustomerName.ShouldBe("Cee");
        cash[1].ShouldBeOfType<AdviserCashLine>().Balance.ShouldBe(10m);
    }

    [Test]
    public void Select_OmitsHoldingsWithQuantityAtOrBelowZero()
    {
        var broker = Guid.Parse("00000000-0000-0000-0000-000000000004");
        var gone = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var negative = Guid.Parse("10000000-0000-0000-0000-000000000002");
        var vti = Guid.Parse("10000000-0000-0000-0000-000000000003");

        var selection = AdviserBalancesRows.Select(
            [
                Account(
                    broker,
                    "Broker",
                    0m,
                    holdings:
                    [
                        Holding(gone, "GONE", 0m, cost: 40m),
                        Holding(negative, "NEG", -1m, cost: 5m),
                        Holding(vti, "VTI", 12.5m, cost: 100m)
                    ])
            ],
            includeClosedAccounts: false);

        selection.Lines.Count.ShouldBe(1);
        var holding = selection.Lines[0].ShouldBeOfType<AdviserHoldingLine>();
        holding.InstrumentId.ShouldBe(vti);
        holding.Symbol.ShouldBe("VTI");
        holding.Quantity.ShouldBe(12.5m);
        holding.CostAmount.ShouldBe(100m);
        holding.CostCurrency.ShouldBe("USD");
        holding.AccountName.ShouldBe("Broker");
        holding.CustomerName.ShouldBe("Cee");
    }

    private static AdviserBalanceHolding Holding(
        Guid instrumentId,
        string symbol,
        decimal quantity,
        decimal cost) =>
        new(instrumentId, symbol, quantity, cost, "USD", PriceAmount: null, PriceCurrency: null, QuoteCurrency: "USD");

    private static AdviserBalanceAccount Account(
        Guid accountId,
        string name,
        decimal cash,
        bool closed = false,
        IReadOnlyList<AdviserBalanceHolding>? holdings = null) =>
        new(
            CustomerId,
            "Cee",
            accountId,
            name,
            "Bank",
            "NZD",
            cash,
            closed,
            holdings ?? []);
}
