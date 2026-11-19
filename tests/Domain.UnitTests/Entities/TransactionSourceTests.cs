using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using MyWealthV2.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Entities;

/// <summary>Transaction source R2, R3, R7, R8: every Transaction stores the source its writer passed.</summary>
public class TransactionSourceTests
{
    private const int TenantId = 7;
    private const int AccountId = 42;
    private const int OtherAccountId = 43;
    private static readonly DateTimeOffset BookedAt = new(2026, 11, 19, 10, 0, 0, TimeSpan.FromHours(13));
    private static readonly Guid Handle = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");

    [Test]
    public void Manual_HasNoId()
    {
        TransactionSource.Manual.Kind.ShouldBe(TransactionSourceKind.Manual);
        TransactionSource.Manual.PublicId.ShouldBeNull();
    }

    [Test]
    public void Seed_HasNoId()
    {
        TransactionSource.Seed.Kind.ShouldBe(TransactionSourceKind.Seed);
        TransactionSource.Seed.PublicId.ShouldBeNull();
    }

    [Test]
    public void Rebalance_KeepsTheHandle()
    {
        var source = TransactionSource.Rebalance(Handle);

        source.Kind.ShouldBe(TransactionSourceKind.Rebalance);
        source.PublicId.ShouldBe(Handle);
    }

    [Test]
    public void Rebalance_EmptyHandle_Throws()
    {
        Should.Throw<DomainException>(() => TransactionSource.Rebalance(Guid.Empty));
    }

    private static IEnumerable<TestCaseData> Factories()
    {
        yield return new TestCaseData(new Func<TransactionSource, Transaction>(Post)).SetArgDisplayNames("Post");
        yield return new TestCaseData(new Func<TransactionSource, Transaction>(InternalTransfer)).SetArgDisplayNames("PostInternalTransfer");
        yield return new TestCaseData(new Func<TransactionSource, Transaction>(FxConversion)).SetArgDisplayNames("PostFxConversion");
        yield return new TestCaseData(new Func<TransactionSource, Transaction>(FxConversionWithAmounts)).SetArgDisplayNames("PostFxConversionWithAmounts");
        yield return new TestCaseData(new Func<TransactionSource, Transaction>(OpeningHoldings)).SetArgDisplayNames("PostOpeningHoldings");
        yield return new TestCaseData(new Func<TransactionSource, Transaction>(Buy)).SetArgDisplayNames("PostBuy");
        yield return new TestCaseData(new Func<TransactionSource, Transaction>(Sell)).SetArgDisplayNames("PostSell");
        yield return new TestCaseData(new Func<TransactionSource, Transaction>(StockIn)).SetArgDisplayNames("PostStockIn");
        yield return new TestCaseData(new Func<TransactionSource, Transaction>(StockOut)).SetArgDisplayNames("PostStockOut");
        yield return new TestCaseData(new Func<TransactionSource, Transaction>(Split)).SetArgDisplayNames("PostSplit");
    }

    [TestCaseSource(nameof(Factories))]
    public void Factory_StoresTheRebalanceSourceItWasGiven(Func<TransactionSource, Transaction> factory)
    {
        var transaction = factory(TransactionSource.Rebalance(Handle));

        transaction.Source.Kind.ShouldBe(TransactionSourceKind.Rebalance);
        transaction.Source.PublicId.ShouldBe(Handle);
    }

    [TestCaseSource(nameof(Factories))]
    public void Factory_StoresTheSeedSourceItWasGiven(Func<TransactionSource, Transaction> factory)
    {
        var transaction = factory(TransactionSource.Seed);

        transaction.Source.Kind.ShouldBe(TransactionSourceKind.Seed);
        transaction.Source.PublicId.ShouldBeNull();
    }

    [TestCaseSource(nameof(Factories))]
    public void Factory_StoresTheManualSourceItWasGiven(Func<TransactionSource, Transaction> factory)
    {
        var transaction = factory(TransactionSource.Manual);

        transaction.Source.Kind.ShouldBe(TransactionSourceKind.Manual);
        transaction.Source.PublicId.ShouldBeNull();
    }

    [Test]
    public void Reverse_StoresTheSourceItWasGiven_NotTheOriginals()
    {
        var original = Buy(TransactionSource.Rebalance(Handle));
        original.Id = 9;

        var reversal = original.Reverse(
            TransactionSource.Manual,
            BookedAt.AddDays(1),
            currentCashSumByAccount: new Dictionary<int, decimal> { [AccountId] = 980m });

        reversal.Source.Kind.ShouldBe(TransactionSourceKind.Manual);
        reversal.Source.PublicId.ShouldBeNull();
        original.Source.Kind.ShouldBe(TransactionSourceKind.Rebalance);
    }

    [Test]
    public void Reverse_WithAnotherSource_StoresThatSource()
    {
        var original = Post(TransactionSource.Manual);
        original.Id = 9;

        var reversal = original.Reverse(
            TransactionSource.Seed,
            BookedAt.AddDays(1),
            currentCashSumByAccount: new Dictionary<int, decimal> { [AccountId] = 100m });

        reversal.Source.Kind.ShouldBe(TransactionSourceKind.Seed);
    }

    private static Transaction Post(TransactionSource source) =>
        Transaction.Post(
            TenantId, AccountId, TransactionType.TransferIn, 100m, "NZD", BookedAt, memo: null, reference: null,
            source, currentCashSum: 0m, existingTransactionCount: 0);

    private static Transaction InternalTransfer(TransactionSource source) =>
        Transaction.PostInternalTransfer(
            TenantId, AccountId, "NZD", CustodyMode.External, OtherAccountId, "NZD", CustodyMode.External,
            Money.Create(10m, "NZD"), BookedAt, memo: null, reference: null, source, fromCurrentCashSum: 100m);

    private static Transaction FxConversion(TransactionSource source) =>
        Transaction.PostFxConversion(
            TenantId, AccountId, "NZD", CustodyMode.External, OtherAccountId, "USD", CustodyMode.External,
            AppliedFxRate.Create("NZD", "USD", 0.5m, new DateOnly(2026, 11, 12), "Frankfurter"),
            Money.Create(100m, "NZD"), computedDecimalPlaces: 2, BookedAt, memo: null, reference: null, source,
            fromCurrentCashSum: 1000m);

    private static Transaction FxConversionWithAmounts(TransactionSource source) =>
        Transaction.PostFxConversionWithAmounts(
            TenantId, AccountId, "NZD", CustodyMode.External, OtherAccountId, "USD", CustodyMode.External,
            Money.Create(100m, "NZD"), Money.Create(50m, "USD"), BookedAt, memo: null, reference: null, source,
            fromCurrentCashSum: 1000m);

    private static Transaction OpeningHoldings(TransactionSource source) =>
        Transaction.PostOpeningHoldings(
            TenantId, AccountId, AccountType.Brokerage, BookedAt, memo: null, reference: null, source,
            [OpeningSecurityLeg.Create(11, 10m, 100m, "USD")]);

    private static Transaction Buy(TransactionSource source) =>
        Transaction.PostBuy(
            TenantId, AccountId, AccountType.Brokerage, "NZD", 11, 2m, Money.Create(10m, "NZD"), fxRate: 1m, BookedAt,
            memo: null, reference: null, source, currentCashSum: 1000m);

    private static Transaction Sell(TransactionSource source) =>
        Transaction.PostSell(
            TenantId, AccountId, AccountType.Brokerage, "NZD", 11, 1m, Money.Create(10m, "NZD"), fxRate: 1m, BookedAt,
            memo: null, reference: null, source, currentCashSum: 0m, currentQuantity: 2m, currentCost: 20m);

    private static Transaction StockIn(TransactionSource source) =>
        Transaction.PostStockIn(
            TenantId, AccountId, AccountType.Brokerage, 11, 2m, 20m, "USD", BookedAt, memo: null, reference: null,
            source);

    private static Transaction StockOut(TransactionSource source) =>
        Transaction.PostStockOut(
            TenantId, AccountId, AccountType.Brokerage, 11, 1m, "USD", BookedAt, memo: null, reference: null, source,
            currentQuantity: 2m, currentCost: 20m);

    private static Transaction Split(TransactionSource source) =>
        Transaction.PostSplit(
            TenantId, AccountId, AccountType.Brokerage, TransactionType.Split, 11, 4m, "USD", BookedAt, memo: null,
            reference: null, source, currentQuantity: 2m, currentCost: 20m);
}
