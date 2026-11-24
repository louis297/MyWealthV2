using MyWealthV2.Domain.Custody;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using MyWealthV2.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.InKindTransfers;

/// <summary>In-kind transfers R37, R38, R49: the Transactions a completed transfer posts, and their source.</summary>
public class InKindTransferPostingTests
{
    private const int TenantId = 3;
    private const int AccountId = 5;
    private const int InstrumentId = 9;
    private static readonly DateTimeOffset At = new(2026, 11, 24, 14, 12, 0, TimeSpan.FromHours(13));
    private static readonly Guid TransferId = Guid.Parse("2c3d4e5f-1111-4a4a-9b9b-5f5f5f5f5f5f");
    private static readonly InKindTransferOrigin Origin = new(TransferId, "Harbour Brokers", "HB-0044172", false,
        new DateOnly(2024, 3, 18), new DateOnly(2026, 11, 24), "w-M12");

    [Test]
    public void InKindTransferSource_KeepsTheTransferId()
    {
        var source = TransactionSource.InKindTransfer(TransferId);

        (source.Kind, source.PublicId).ShouldBe((TransactionSourceKind.InKindTransfer, (Guid?)TransferId));
    }

    [Test]
    public void InKindTransferSource_WithAnEmptyId_Throws() =>
        Should.Throw<DomainException>(() => TransactionSource.InKindTransfer(Guid.Empty));

    [Test]
    public void PostTransferIn_IsAStockIn_WithOneLeg_TheCost_TheMemo_AndTheDetail()
    {
        var transaction = TransferIn();

        transaction.Type.ShouldBe(TransactionType.StockIn);
        transaction.BookedAt.ShouldBe(At.ToUniversalTime());
        transaction.BookedAt.Offset.ShouldBe(TimeSpan.Zero);
        (transaction.Memo, transaction.Reference).ShouldBe(("Transfer in", (string?)null));
        transaction.Source.ShouldBe(TransactionSource.InKindTransfer(TransferId));
        transaction.CashLegs.ShouldBeEmpty();
        transaction.SecurityLegs.Select(l => (l.AccountId, l.InstrumentId, l.Quantity, l.CostAmount, l.CostCurrency))
            .ShouldBe([(AccountId, InstrumentId, 100m, 9500m, "USD")]);
        var detail = transaction.InKindTransferDetail.ShouldNotBeNull();
        (detail.TenantId, detail.TransferPublicId, detail.CounterpartyName, detail.CounterpartyAccountMasked)
            .ShouldBe((TenantId, TransferId, "Harbour Brokers", "****4172"));
        (detail.ChangesBeneficialOwner, detail.AcquiredOn, detail.SettlementDate, detail.CustodianReference)
            .ShouldBe((false, (DateOnly?)new DateOnly(2024, 3, 18), new DateOnly(2026, 11, 24), "w-M12"));
        transaction.DomainEvents.ShouldContain(e => e is MyWealthV2.Domain.Events.TransactionPosted);
    }

    [Test]
    public void PostTransferIn_WithAnotherSource_Throws() =>
        Should.Throw<DomainException>(() => Transaction.PostTransferIn(TenantId, AccountId, AccountType.Brokerage, InstrumentId,
            100m, 9500m, "USD", At, TransactionSource.Manual, Origin));

    [Test]
    public void PostTransferIn_WithoutADetail_Throws() =>
        Should.Throw<DomainException>(() => Transaction.PostTransferIn(TenantId, AccountId, AccountType.Brokerage, InstrumentId,
            100m, 9500m, "USD", At, TransactionSource.InKindTransfer(TransferId), null!));

    [Test]
    public void PostTransferOut_IsAStockOut_ReleasingAverageCost()
    {
        var transaction = Transaction.PostTransferOut(TenantId, AccountId, AccountType.Brokerage, InstrumentId, 40m, "USD", At,
            TransactionSource.InKindTransfer(TransferId), Origin with { AcquiredOn = null }, 100m, 9500m);

        transaction.Type.ShouldBe(TransactionType.StockOut);
        transaction.Memo.ShouldBe("Transfer out");
        transaction.CashLegs.ShouldBeEmpty();
        transaction.SecurityLegs.Select(l => (l.Quantity, l.CostAmount)).ShouldBe([(-40m, -3800m)]);
        transaction.InKindTransferDetail.ShouldNotBeNull().AcquiredOn.ShouldBeNull();
    }

    [Test]
    public void PostTransferOut_ForMoreThanTheHolding_Throws() =>
        Should.Throw<DomainException>(() => Transaction.PostTransferOut(TenantId, AccountId, AccountType.Brokerage, InstrumentId,
            101m, "USD", At, TransactionSource.InKindTransfer(TransferId), Origin, 100m, 9500m));

    [Test]
    public void PostTransferOut_WithAnotherSource_Throws() =>
        Should.Throw<DomainException>(() => Transaction.PostTransferOut(TenantId, AccountId, AccountType.Brokerage, InstrumentId,
            40m, "USD", At, TransactionSource.Manual, Origin, 100m, 9500m));

    [Test]
    public void Reverse_CopiesTheDetail()
    {
        var transaction = TransferIn();

        var reversal = transaction.Reverse(TransactionSource.InKindTransfer(TransferId), At, new Dictionary<int, decimal>(),
            new Dictionary<(int, int), decimal> { [(AccountId, InstrumentId)] = 100m });

        var detail = reversal.InKindTransferDetail.ShouldNotBeNull();
        detail.ShouldNotBeSameAs(transaction.InKindTransferDetail);
        (detail.TransferPublicId, detail.CounterpartyAccountMasked, detail.CustodianReference).ShouldBe((TransferId, "****4172", "w-M12"));
    }

    [Test]
    public void OtherTransactions_HaveNoInKindTransferDetail() =>
        Transaction.PostStockIn(TenantId, AccountId, AccountType.Brokerage, InstrumentId, 1m, 1m, "USD", At, null, null,
            TransactionSource.Manual).InKindTransferDetail.ShouldBeNull();

    private static Transaction TransferIn() =>
        Transaction.PostTransferIn(TenantId, AccountId, AccountType.Brokerage, InstrumentId, 100m, 9500m, "usd", At,
            TransactionSource.InKindTransfer(TransferId), Origin);
}
