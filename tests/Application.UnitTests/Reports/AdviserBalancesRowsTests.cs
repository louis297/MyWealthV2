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

    [Test]
    public void Select_PricesHoldingsInQuoteCurrencyWithoutFx()
    {
        var broker = Guid.Parse("00000000-0000-0000-0000-000000000005");
        var missing = Guid.Parse("10000000-0000-0000-0000-000000000011");
        var flat = Guid.Parse("10000000-0000-0000-0000-000000000012");
        var foreign = Guid.Parse("10000000-0000-0000-0000-000000000013");
        var vti = Guid.Parse("10000000-0000-0000-0000-000000000014");

        var selection = AdviserBalancesRows.Select(
            [
                Account(
                    broker,
                    "Broker",
                    10m,
                    holdings:
                    [
                        Holding(missing, "CASHFUND", 4m, 4m),
                        Holding(flat, "FLAT", 2m, 2m, price: 0m, priceCurrency: "USD"),
                        Holding(foreign, "EURX", 3m, 30m, price: 8m, priceCurrency: "EUR"),
                        Holding(vti, "VTI", 12.5m, 100m, price: 20m, priceCurrency: "USD")
                    ])
            ],
            includeClosedAccounts: false);

        var holdings = selection.Lines.OfType<AdviserHoldingLine>().ToList();
        holdings.Select(line => line.Symbol).ShouldBe(["CASHFUND", "FLAT", "EURX", "VTI"]);
        holdings[0].MarketValue.ShouldBe(0m);
        holdings[0].MarketCurrency.ShouldBe("USD");
        holdings[1].MarketValue.ShouldBe(0m);
        holdings[1].MarketCurrency.ShouldBe("USD");
        holdings[2].MarketValue.ShouldBe(0m);
        holdings[2].MarketCurrency.ShouldBe("USD");
        holdings[3].MarketValue.ShouldBe(250m);
        holdings[3].MarketCurrency.ShouldBe("USD");

        var cash = selection.Lines.OfType<AdviserCashLine>().Single();
        cash.Currency.ShouldBe("NZD");
        cash.Balance.ShouldBe(10m);
    }

    [Test]
    public void Select_OmitsClosedAccountsUnlessRequested()
    {
        var open = Guid.Parse("00000000-0000-0000-0000-000000000006");
        var closed = Guid.Parse("00000000-0000-0000-0000-000000000007");
        var vti = Guid.Parse("10000000-0000-0000-0000-000000000021");
        var accounts = new[]
        {
            Account(open, "Open", 5m),
            Account(
                closed,
                "Closed",
                7m,
                closed: true,
                holdings: [Holding(vti, "VTI", 1m, 10m, price: 4m, priceCurrency: "USD")])
        };

        AdviserBalancesRows.Select(accounts, includeClosedAccounts: false)
            .Lines.Select(line => line.AccountId)
            .ShouldBe([open]);

        var included = AdviserBalancesRows.Select(accounts, includeClosedAccounts: true).Lines;
        included.OfType<AdviserCashLine>().Select(line => line.Balance).ShouldBe([5m, 7m]);
        included.OfType<AdviserHoldingLine>().Single().Symbol.ShouldBe("VTI");
    }

    [Test]
    public void Select_SortsLinesAndSubtotalsByCurrency()
    {
        var annEarly = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var annLate = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var cee = Guid.Parse("00000000-0000-0000-0000-000000000003");
        var brokerEarly = Guid.Parse("20000000-0000-0000-0000-000000000001");
        var brokerLate = Guid.Parse("20000000-0000-0000-0000-000000000002");
        var zebra = Guid.Parse("20000000-0000-0000-0000-000000000003");
        var everyday = Guid.Parse("20000000-0000-0000-0000-000000000004");
        var closedAccount = Guid.Parse("20000000-0000-0000-0000-000000000005");
        var vtiEarly = Guid.Parse("30000000-0000-0000-0000-000000000001");
        var vtiLate = Guid.Parse("30000000-0000-0000-0000-000000000002");
        var zzz = Guid.Parse("30000000-0000-0000-0000-000000000003");

        var selection = AdviserBalancesRows.Select(
            [
                Account(brokerLate, "Broker", 1m, customerId: annLate, customerName: "Ann", holdings:
                [
                    Holding(vtiLate, "VTI", 2m, 20m, price: 5m, priceCurrency: "USD"),
                    Holding(vtiEarly, "VTI", 1m, 10m, price: 4m, priceCurrency: "USD")
                ]),
                Account(zebra, "Zebra", 0m, customerId: annEarly, customerName: "Ann", holdings:
                [
                    Holding(zzz, "ZZZ", 1m, 1m)
                ]),
                Account(brokerEarly, "Broker", 8m, customerId: annEarly, customerName: "Ann"),
                Account(everyday, "Everyday", 3m, customerId: cee, customerName: "Cee", currency: "USD"),
                Account(closedAccount, "Shut", 100m, closed: true, customerId: annEarly, customerName: "Ann")
            ],
            includeClosedAccounts: false);

        selection.Lines.Select(Describe).ShouldBe(
        [
            "cash Ann 00000000-0000-0000-0000-000000000001 Broker 20000000-0000-0000-0000-000000000001",
            "holding Ann 00000000-0000-0000-0000-000000000001 Zebra ZZZ",
            "cash Ann 00000000-0000-0000-0000-000000000002 Broker 20000000-0000-0000-0000-000000000002",
            "holding Ann 00000000-0000-0000-0000-000000000002 Broker VTI 30000000-0000-0000-0000-000000000001",
            "holding Ann 00000000-0000-0000-0000-000000000002 Broker VTI 30000000-0000-0000-0000-000000000002",
            "cash Cee 00000000-0000-0000-0000-000000000003 Everyday 20000000-0000-0000-0000-000000000004"
        ]);
        selection.Subtotals.ShouldBe(
        [
            new AdviserBalanceSubtotal("NZD", 9m, 0m),
            new AdviserBalanceSubtotal("USD", 3m, 14m)
        ]);
    }

    private static string Describe(AdviserBalanceLine line) => line switch
    {
        AdviserCashLine cash =>
            $"cash {cash.CustomerName} {cash.CustomerId} {cash.AccountName} {cash.AccountId}",
        AdviserHoldingLine holding =>
            $"holding {holding.CustomerName} {holding.CustomerId} {holding.AccountName} {holding.Symbol} {holding.InstrumentId}",
        _ => throw new InvalidOperationException(line.GetType().Name)
    };

    private static AdviserBalanceHolding Holding(
        Guid instrumentId,
        string symbol,
        decimal quantity,
        decimal cost,
        decimal? price = null,
        string? priceCurrency = null,
        string quoteCurrency = "USD") =>
        new(instrumentId, symbol, quantity, cost, "USD", price, priceCurrency, quoteCurrency);

    private static AdviserBalanceAccount Account(
        Guid accountId,
        string name,
        decimal cash,
        bool closed = false,
        IReadOnlyList<AdviserBalanceHolding>? holdings = null,
        Guid? customerId = null,
        string customerName = "Cee",
        string currency = "NZD") =>
        new(
            customerId ?? CustomerId,
            customerName,
            accountId,
            name,
            "Bank",
            currency,
            cash,
            closed,
            holdings ?? []);
}
