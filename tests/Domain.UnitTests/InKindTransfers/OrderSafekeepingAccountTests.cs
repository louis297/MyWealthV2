using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using MyWealthV2.Domain.Orders;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.InKindTransfers;

/// <summary>In-kind transfers R46: an order stores the safekeeping account it was placed with, and never changes it.</summary>
public class OrderSafekeepingAccountTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 24, 2, 0, 0, TimeSpan.Zero);
    private static readonly Currency Usd = Currency.Create("USD", "US Dollar", 2);

    [Test]
    public void Place_WithASafekeepingAccount_KeepsItAndItsNumber()
    {
        var safekeeping = SafekeepingAccount.Create(7, Usd, "Simulator Custody", "Nominees", "SIM-DEMO-CUSTODY-USD", "simulator");
        safekeeping.Id = 4;

        var order = Place(safekeeping);

        (order.SafekeepingAccountId, order.SafekeepingAccountNumber).ShouldBe(((int?)4, "SIM-DEMO-CUSTODY-USD"));
    }

    [Test]
    public void Place_WithoutOne_HasNone() =>
        (Place(null).SafekeepingAccountId, Place(null).SafekeepingAccountNumber).ShouldBe(((int?)null, (string?)null));

    [Test]
    public void Place_WithAnotherTenantsOrAnotherCurrencysSafekeepingAccount_Throws()
    {
        var otherTenant = SafekeepingAccount.Create(8, Usd, "Custody", "Nominees", "SK-1", "simulator");
        var nzd = SafekeepingAccount.Create(7, Currency.Create("NZD", "New Zealand Dollar", 2), "Custody", "Nominees", "SK-2", "simulator");

        Should.Throw<DomainException>(() => Place(otherTenant));
        Should.Throw<DomainException>(() => Place(nzd));
    }

    private static Order Place(SafekeepingAccount? safekeeping) =>
        Order.Place(7, 3, 11, OrderSide.Buy, 10m, "USD", 100m, new DateOnly(2026, 11, 24), OrderFeeTerms.None, "simulator", null,
            21, At, safekeepingAccount: safekeeping);
}
