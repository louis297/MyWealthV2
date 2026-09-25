using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Events;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Entities;

public class TransactionTests
{
    [Test]
    public void Post_TransferIn_StoresSignedAmountAndRaisesTransactionPosted()
    {
        var bookedAt = new DateTimeOffset(2026, 9, 24, 9, 0, 0, TimeSpan.FromHours(12));

        var transaction = Transaction.Post(
            tenantId: 1,
            accountId: 2,
            type: TransactionType.TransferIn,
            amount: 100.00m,
            accountCurrency: "NZD",
            bookedAt: bookedAt,
            memo: "Salary",
            reference: null,
            currentCashSum: 0m,
            existingTransactionCount: 0);

        transaction.TenantId.ShouldBe(1);
        transaction.AccountId.ShouldBe(2);
        transaction.Type.ShouldBe(TransactionType.TransferIn);
        transaction.BookedAt.ShouldBe(bookedAt);
        transaction.Memo.ShouldBe("Salary");
        transaction.Reference.ShouldBeNull();
        transaction.OriginalTransactionId.ShouldBeNull();
        transaction.PublicId.ShouldNotBe(Guid.Empty);
        transaction.CashLeg.ShouldNotBeNull();
        transaction.CashLeg!.Amount.ShouldBe(100.00m);
        transaction.CashLeg.Currency.ShouldBe("NZD");
        transaction.DomainEvents.OfType<TransactionPosted>().ShouldHaveSingleItem();
    }
}
