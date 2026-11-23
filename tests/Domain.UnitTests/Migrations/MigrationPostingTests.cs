using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using MyWealthV2.Domain.Migrations;
using MyWealthV2.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Migrations;

/// <summary>Migration batches R22, R27: the two Transactions a batch writes and their source.</summary>
public class MigrationPostingTests
{
    private const int TenantId = 3;
    private const int AccountId = 5;
    private static readonly DateTimeOffset At = new(2026, 11, 24, 2, 15, 0, TimeSpan.FromHours(13));
    private static readonly Guid BatchId = Guid.Parse("7b1ae0a4-1c2c-4d55-9f58-2b0c3b4f1c11");
    private static readonly MigrationOrigin Origin = new(new DateOnly(2026, 11, 20), "Acme Wealth");

    [Test]
    public void MigrationSource_KeepsTheBatchId()
    {
        var source = TransactionSource.Migration(BatchId);

        (source.Kind, source.PublicId).ShouldBe((TransactionSourceKind.Migration, (Guid?)BatchId));
    }

    [Test]
    public void MigrationSource_WithAnEmptyId_Throws()
    {
        Should.Throw<DomainException>(() => TransactionSource.Migration(Guid.Empty));
    }

    [Test]
    public void PostMigratedCash_IsAnOpening_WithOnePrincipalLeg_TheMemo_TheReference_AndTheDetail()
    {
        var transaction = Transaction.PostMigratedCash(TenantId, AccountId, "usd", 150000m, At, " ACME-004417 ",
            TransactionSource.Migration(BatchId), Origin);

        transaction.Type.ShouldBe(TransactionType.Opening);
        transaction.BookedAt.ShouldBe(At.ToUniversalTime());
        transaction.BookedAt.Offset.ShouldBe(TimeSpan.Zero);
        (transaction.Memo, transaction.Reference).ShouldBe(("Opening balance", "ACME-004417"));
        transaction.Source.ShouldBe(TransactionSource.Migration(BatchId));
        transaction.CashLegs.Select(l => (l.AccountId, l.Amount, l.Currency, l.Role))
            .ShouldBe([(AccountId, 150000m, "USD", CashLegRole.Principal)]);
        transaction.SecurityLegs.ShouldBeEmpty();
        (transaction.MigrationDetail!.TenantId, transaction.MigrationDetail.AsOfDate, transaction.MigrationDetail.PreviousProvider)
            .ShouldBe((TenantId, new DateOnly(2026, 11, 20), "Acme Wealth"));
        transaction.DomainEvents.ShouldContain(e => e is MyWealthV2.Domain.Events.TransactionPosted);
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void PostMigratedCash_AnAmountThatIsNotAbove0_Throws(decimal amount)
    {
        Should.Throw<DomainException>(() => Transaction.PostMigratedCash(TenantId, AccountId, "USD", amount, At, null,
            TransactionSource.Migration(BatchId), Origin));
    }

    [Test]
    public void PostMigratedCash_WithAnotherSource_Throws()
    {
        Should.Throw<DomainException>(() => Transaction.PostMigratedCash(TenantId, AccountId, "USD", 1m, At, null,
            TransactionSource.Manual, Origin));
    }

    [Test]
    public void PostMigratedHoldings_IsAnOpeningHoldings_WithTheLegs_TheMemo_AndTheDetail()
    {
        var transaction = Transaction.PostMigratedHoldings(TenantId, AccountId, AccountType.Brokerage, At, null,
            TransactionSource.Migration(BatchId), Origin,
            [OpeningSecurityLeg.Create(9, 800m, 176000m, "USD"), OpeningSecurityLeg.Create(10, 1m, 0m, "USD")]);

        transaction.Type.ShouldBe(TransactionType.OpeningHoldings);
        (transaction.Memo, transaction.Reference).ShouldBe(("Opening balance", (string?)null));
        transaction.CashLegs.ShouldBeEmpty();
        transaction.SecurityLegs.Select(l => (l.AccountId, l.InstrumentId, l.Quantity, l.CostAmount, l.CostCurrency))
            .ShouldBe([(AccountId, 9, 800m, 176000m, "USD"), (AccountId, 10, 1m, 0m, "USD")]);
        transaction.Source.Kind.ShouldBe(TransactionSourceKind.Migration);
        transaction.MigrationDetail!.PreviousProvider.ShouldBe("Acme Wealth");
    }

    [Test]
    public void PostMigratedHoldings_WithNoLeg_OrAnotherSource_Throws()
    {
        Should.Throw<DomainException>(() => Transaction.PostMigratedHoldings(TenantId, AccountId, AccountType.Brokerage, At,
            null, TransactionSource.Migration(BatchId), Origin, []));
        Should.Throw<DomainException>(() => Transaction.PostMigratedHoldings(TenantId, AccountId, AccountType.Brokerage, At,
            null, TransactionSource.Seed, Origin, [OpeningSecurityLeg.Create(9, 1m, 1m, "USD")]));
    }

    [Test]
    public void Reverse_OfAMigratedOpening_CopiesTheDetail()
    {
        var opening = Transaction.PostMigratedCash(TenantId, AccountId, "USD", 10m, At, null,
            TransactionSource.Migration(BatchId), Origin);

        var reversal = opening.Reverse(TransactionSource.Manual, At, new Dictionary<int, decimal> { [AccountId] = 10m });

        reversal.MigrationDetail.ShouldNotBeNull();
        reversal.MigrationDetail.ShouldNotBeSameAs(opening.MigrationDetail);
        (reversal.MigrationDetail.AsOfDate, reversal.MigrationDetail.PreviousProvider)
            .ShouldBe((new DateOnly(2026, 11, 20), "Acme Wealth"));
    }
}
