using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Entities;

/// <summary>fixes/utc-instants.md B3: every Transaction factory stores bookedAt as the same instant in UTC.</summary>
public class TransactionBookedAtUtcTests
{
    private static readonly DateTimeOffset BookedAt = new(2026, 11, 14, 9, 0, 0, TimeSpan.FromHours(13));

    private static IEnumerable<TestCaseData> Factories()
    {
        yield return Case("Post", () => Transaction.Post(
            1, 2, TransactionType.TransferIn, 100m, "NZD", BookedAt, null, null,
            currentCashSum: 0m, existingTransactionCount: 0));
        yield return Case("PostInternalTransfer", () => Transaction.PostInternalTransfer(
            1, 2, "NZD", 3, "NZD", Money.Create(10m, "NZD"), BookedAt, null, null, fromCurrentCashSum: 100m));
        yield return Case("PostFxConversion", () => Transaction.PostFxConversion(
            1, 2, "NZD", 3, "USD",
            AppliedFxRate.Create("NZD", "USD", 0.5912m, new DateOnly(2026, 11, 13), "Frankfurter"),
            Money.Create(100m, "NZD"), 2, BookedAt, null, null, fromCurrentCashSum: 500m));
        yield return Case("PostOpeningHoldings", () => Transaction.PostOpeningHoldings(
            1, 2, AccountType.Brokerage, BookedAt, null, null, [OpeningSecurityLeg.Create(11, 1m, 10m, "USD")]));
        yield return Case("PostBuy", () => Transaction.PostBuy(
            1, 2, AccountType.Brokerage, "NZD", 11, 1m, Money.Create(10m, "USD"), 1.5m, BookedAt, null, null,
            currentCashSum: 100m));
        yield return Case("PostSell", () => Transaction.PostSell(
            1, 2, AccountType.Brokerage, "NZD", 11, 1m, Money.Create(10m, "USD"), 1.5m, BookedAt, null, null,
            currentCashSum: 0m, currentQuantity: 2m, currentCost: 20m));
        yield return Case("PostStockIn", () => Transaction.PostStockIn(
            1, 2, AccountType.Brokerage, 11, 1m, 10m, "USD", BookedAt, null, null));
        yield return Case("PostStockOut", () => Transaction.PostStockOut(
            1, 2, AccountType.Brokerage, 11, 1m, "USD", BookedAt, null, null, currentQuantity: 2m, currentCost: 20m));
        yield return Case("PostSplit", () => Transaction.PostSplit(
            1, 2, AccountType.Brokerage, TransactionType.Split, 11, 4m, "USD", BookedAt, null, null,
            currentQuantity: 2m, currentCost: 20m));
        yield return Case("Reverse", () => Transaction.Post(
                1, 2, TransactionType.TransferIn, 100m, "NZD", BookedAt.AddDays(-1), null, null,
                currentCashSum: 0m, existingTransactionCount: 0)
            .Reverse(BookedAt, new Dictionary<int, decimal> { [2] = 100m }));
    }

    [TestCaseSource(nameof(Factories))]
    public void Factory_StoresBookedAtInUtc(Func<Transaction> create)
    {
        var transaction = create();

        transaction.BookedAt.ShouldBe(BookedAt);
        transaction.BookedAt.Offset.ShouldBe(TimeSpan.Zero);
    }

    private static TestCaseData Case(string name, Func<Transaction> create) =>
        new TestCaseData(create).SetArgDisplayNames(name);
}
