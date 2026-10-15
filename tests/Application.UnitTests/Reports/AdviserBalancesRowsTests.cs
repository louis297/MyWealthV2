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
