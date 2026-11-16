using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Entities;

/// <summary>fixes/ledger-write-guards.md Decision 1: reverse only the latest security activity on a holding.</summary>
public class ReverseOrderTests
{
    private const string LaterActivityMessage =
        "A later transaction changed this holding. Reverse the later transactions first.";

    private static readonly DateTimeOffset BookedAt = new(2026, 10, 11, 10, 0, 0, TimeSpan.Zero);

    [Test]
    public void Reverse_SecurityTransactionWithLaterActivity_Throws()
    {
        var stockIn = StockIn();

        var exception = Should.Throw<DomainException>(() => stockIn.Reverse(
            BookedAt.AddDays(1),
            currentCashSumByAccount: new Dictionary<int, decimal>(),
            currentQuantity: new Dictionary<(int, int), decimal> { [(2, 11)] = 150m },
            hasLaterSecurityActivity: true));

        exception.Message.ShouldBe(LaterActivityMessage);
        stockIn.ReversedByTransactionId.ShouldBeNull();
    }

    [Test]
    public void Reverse_SecurityTransactionWithoutLaterActivity_BuildsTheReversal()
    {
        var stockIn = StockIn();

        var reversal = stockIn.Reverse(
            BookedAt.AddDays(1),
            currentCashSumByAccount: new Dictionary<int, decimal>(),
            currentQuantity: new Dictionary<(int, int), decimal> { [(2, 11)] = 100m },
            hasLaterSecurityActivity: false);

        reversal.Type.ShouldBe(TransactionType.Reversal);
        var leg = reversal.SecurityLegs.ShouldHaveSingleItem();
        leg.Quantity.ShouldBe(-100m);
        leg.CostAmount.ShouldBe(-1000m);
    }

    [Test]
    public void Reverse_CashOnlyTransaction_IgnoresLaterActivity()
    {
        var transferIn = Transaction.Post(
            tenantId: 1,
            accountId: 2,
            type: TransactionType.TransferIn,
            amount: 100m,
            accountCurrency: "USD",
            bookedAt: BookedAt,
            memo: null,
            reference: null,
            currentCashSum: 0m,
            existingTransactionCount: 0);

        var reversal = transferIn.Reverse(
            BookedAt.AddDays(1),
            currentCashSumByAccount: new Dictionary<int, decimal> { [2] = 100m },
            hasLaterSecurityActivity: true);

        reversal.Type.ShouldBe(TransactionType.Reversal);
        reversal.CashLegs.Single().Amount.ShouldBe(-100m);
    }

    private static Transaction StockIn() =>
        Transaction.PostStockIn(
            tenantId: 1,
            accountId: 2,
            accountType: AccountType.Brokerage,
            instrumentId: 11,
            quantity: 100m,
            cost: 1000m,
            quoteCurrency: "USD",
            bookedAt: BookedAt,
            memo: null,
            reference: null);
}
