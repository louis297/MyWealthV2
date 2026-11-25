using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Events;
using MyWealthV2.Domain.Exceptions;
using MyWealthV2.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Entities;

/// <summary>Adjustments §5, R6–R11, R13, R18: the Adjustment factory and its reversal.</summary>
public class AdjustmentTests
{
    private const int TenantId = 3;
    private const int AccountId = 51;
    private const int InstrumentId = 61;
    private static readonly DateTimeOffset BookedAt = new(2026, 11, 19, 3, 0, 0, TimeSpan.Zero);

    [Test]
    public void PostAdjustment_CashOnly_BuildsOnePrincipalCashLegAndTheDetail()
    {
        var transaction = Adjust(amount: -0.60m, currentCashSum: 10m);

        transaction.Type.ShouldBe(TransactionType.Adjustment);
        transaction.TenantId.ShouldBe(TenantId);
        transaction.BookedAt.ShouldBe(BookedAt);
        transaction.Memo.ShouldBe("Fill price was wrong");
        transaction.PublicId.ShouldNotBe(Guid.Empty);
        transaction.SecurityLegs.ShouldBeEmpty();
        var leg = transaction.CashLegs.ShouldHaveSingleItem();
        leg.AccountId.ShouldBe(AccountId);
        leg.Amount.ShouldBe(-0.60m);
        leg.Currency.ShouldBe("USD");
        leg.Role.ShouldBe(CashLegRole.Principal);
        leg.FeeDetail.ShouldBeNull();
        transaction.FeeDetails.ShouldBeEmpty();
        transaction.FxDetail.ShouldBeNull();
        transaction.DomainEvents.OfType<TransactionPosted>().ShouldHaveSingleItem();
    }

    [Test]
    public void PostAdjustment_SecurityOnly_StoresTheChangesAsEntered()
    {
        var transaction = Adjust(security: Holding(quantityChange: 10m, costChange: 0m, currentQuantity: 10m, currentCost: 251m));

        transaction.CashLegs.ShouldBeEmpty();
        var leg = transaction.SecurityLegs.ShouldHaveSingleItem();
        leg.AccountId.ShouldBe(AccountId);
        leg.InstrumentId.ShouldBe(InstrumentId);
        leg.Quantity.ShouldBe(10m);
        leg.CostAmount.ShouldBe(0m);
        leg.CostCurrency.ShouldBe("USD");
    }

    [Test]
    public void PostAdjustment_CashAndSecurity_BuildsBothLegsOnTheOneAccount()
    {
        var transaction = Adjust(
            amount: -0.60m,
            currentCashSum: 10m,
            security: Holding(quantityChange: 0m, costChange: 0.60m, currentQuantity: 10m, currentCost: 251m));

        transaction.CashLegs.ShouldHaveSingleItem().Amount.ShouldBe(-0.60m);
        var leg = transaction.SecurityLegs.ShouldHaveSingleItem();
        leg.Quantity.ShouldBe(0m);
        leg.CostAmount.ShouldBe(0.60m);
        transaction.CashLegs.Select(cash => cash.AccountId)
            .Concat(transaction.SecurityLegs.Select(security => security.AccountId))
            .ShouldAllBe(id => id == AccountId);
    }

    [Test]
    public void PostAdjustment_NewHolding_FromZero_IsAllowed()
    {
        var transaction = Adjust(security: Holding(quantityChange: 5m, costChange: 120m, currentQuantity: 0m, currentCost: 0m));

        transaction.SecurityLegs.ShouldHaveSingleItem().Quantity.ShouldBe(5m);
    }

    [Test]
    public void PostAdjustment_QuantityAndCostToZero_IsAllowed()
    {
        var transaction = Adjust(security: Holding(quantityChange: -10m, costChange: -251m, currentQuantity: 10m, currentCost: 251m));

        var leg = transaction.SecurityLegs.ShouldHaveSingleItem();
        leg.Quantity.ShouldBe(-10m);
        leg.CostAmount.ShouldBe(-251m);
    }

    [Test]
    public void PostAdjustment_NeitherCashNorSecurity_Throws()
    {
        Should.Throw<DomainException>(() => Adjust());
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void PostAdjustment_BlankMemo_Throws(string? memo)
    {
        Should.Throw<DomainException>(() => Adjust(amount: 5m, memo: memo));
    }

    [Test]
    public void PostAdjustment_ZeroAmount_Throws()
    {
        Should.Throw<DomainException>(() => Adjust(amount: 0m, currentCashSum: 10m));
    }

    [Test]
    public void PostAdjustment_BothChangesZero_Throws()
    {
        Should.Throw<DomainException>(() =>
            Adjust(security: Holding(quantityChange: 0m, costChange: 0m, currentQuantity: 10m, currentCost: 251m)));
    }

    [Test]
    public void PostAdjustment_CashSumBelowZero_Throws()
    {
        Should.Throw<DomainException>(() => Adjust(amount: -10.01m, currentCashSum: 10m));
    }

    [Test]
    public void PostAdjustment_CashSumToZero_IsAllowed()
    {
        Adjust(amount: -10m, currentCashSum: 10m).CashLegs.ShouldHaveSingleItem().Amount.ShouldBe(-10m);
    }

    [TestCase(-11, 0)]
    [TestCase(0, -252)]
    public void PostAdjustment_QuantityOrCostBelowZero_ThrowsWithTheHoldingMessage(decimal quantityChange, decimal costChange)
    {
        var exception = Should.Throw<DomainException>(() =>
            Adjust(security: Holding(quantityChange, costChange, currentQuantity: 10m, currentCost: 251m)));

        exception.Message.ShouldBe(Transaction.AdjustmentHoldingMessage);
    }

    [Test]
    public void PostAdjustment_QuantityToZeroWithCostLeft_ThrowsWithTheHoldingMessage()
    {
        var exception = Should.Throw<DomainException>(() =>
            Adjust(security: Holding(quantityChange: -10m, costChange: 0m, currentQuantity: 10m, currentCost: 251m)));

        exception.Message.ShouldBe(Transaction.AdjustmentHoldingMessage);
    }

    [Test]
    public void PostAdjustment_CostOnlyWithoutAHolding_ThrowsWithTheHoldingMessage()
    {
        var exception = Should.Throw<DomainException>(() =>
            Adjust(security: Holding(quantityChange: 0m, costChange: 5m, currentQuantity: 0m, currentCost: 0m)));

        exception.Message.ShouldBe(Transaction.AdjustmentHoldingMessage);
    }

    [TestCase(AccountType.Bank)]
    [TestCase(AccountType.Cash)]
    public void PostAdjustment_SecurityPartOnABankOrCashAccount_Throws(AccountType accountType)
    {
        Should.Throw<DomainException>(() => Adjust(
            accountType: accountType,
            security: Holding(quantityChange: 5m, costChange: 120m, currentQuantity: 0m, currentCost: 0m)));
    }

    [TestCase(AccountType.Bank)]
    [TestCase(AccountType.Cash)]
    public void PostAdjustment_CashPartOnABankOrCashAccount_IsAllowed(AccountType accountType)
    {
        Adjust(accountType: accountType, amount: 5m).CashLegs.ShouldHaveSingleItem();
    }

    [TestCase(AdjustmentReason.EntryError)]
    [TestCase(AdjustmentReason.CorporateAction)]
    [TestCase(AdjustmentReason.ReconciliationBreak)]
    [TestCase(AdjustmentReason.Other)]
    public void PostAdjustment_TheDetailHoldsTheReason(AdjustmentReason reason)
    {
        var transaction = Adjust(amount: 5m, reason: reason);

        transaction.AdjustmentDetail.ShouldNotBeNull();
        transaction.AdjustmentDetail.Reason.ShouldBe(reason);
        transaction.AdjustmentDetail.TenantId.ShouldBe(TenantId);
    }

    [Test]
    public void PostAdjustment_TheSourceIsTheOnePassed()
    {
        var handle = Guid.NewGuid();

        var manual = Adjust(amount: 5m);
        var other = Adjust(amount: 5m, source: TransactionSource.Rebalance(handle));

        manual.Source.ShouldBe(TransactionSource.Manual);
        other.Source.Kind.ShouldBe(TransactionSourceKind.Rebalance);
        other.Source.PublicId.ShouldBe(handle);
    }

    [Test]
    public void Reverse_OfAnAdjustment_Throws_WhateverItsSource()
    {
        // Adjustment review R15: an adjustment is corrected by a request for the opposite change, never reversed.
        foreach (var source in new[] { TestSources.Manual, TransactionSource.AdjustmentRequest(Guid.NewGuid()) })
        {
            var original = Transaction.PostAdjustment(
                TenantId, AccountId, AccountType.Brokerage, "USD", AdjustmentReason.ReconciliationBreak, "Memo", null,
                BookedAt, source, -0.60m, 10m,
                Holding(quantityChange: 2m, costChange: 0.60m, currentQuantity: 10m, currentCost: 251m));
            original.Id = 9;

            Should.Throw<DomainException>(() => original.Reverse(
                    TestSources.Manual,
                    BookedAt.AddHours(1),
                    new Dictionary<int, decimal> { [AccountId] = 9.40m },
                    new Dictionary<(int AccountId, int InstrumentId), decimal> { [(AccountId, InstrumentId)] = 12m }))
                .Message.ShouldBe(Transaction.AdjustmentReverseMessage);
        }
    }

    [Test]
    public void Reverse_OfAnotherType_HasNoAdjustmentDetail()
    {
        var original = Transaction.Post(
            TenantId, AccountId, TransactionType.TransferIn, 10m, "USD", BookedAt, null, null, TestSources.Manual, 0m, 0);
        original.Id = 10;

        var reversal = original.Reverse(TestSources.Manual, BookedAt.AddHours(1), new Dictionary<int, decimal> { [AccountId] = 10m });

        original.AdjustmentDetail.ShouldBeNull();
        reversal.AdjustmentDetail.ShouldBeNull();
    }

    private static AdjustmentHoldingChange Holding(
        decimal quantityChange,
        decimal costChange,
        decimal currentQuantity,
        decimal currentCost) =>
        new(InstrumentId, "USD", quantityChange, costChange, currentQuantity, currentCost);

    private static Transaction Adjust(
        decimal? amount = null,
        decimal currentCashSum = 0m,
        AdjustmentHoldingChange? security = null,
        AccountType accountType = AccountType.Brokerage,
        string? memo = "Fill price was wrong",
        AdjustmentReason reason = AdjustmentReason.EntryError,
        TransactionSource? source = null) =>
        Transaction.PostAdjustment(
            TenantId,
            AccountId,
            accountType,
            "USD",
            reason,
            memo,
            null,
            BookedAt,
            source ?? TestSources.Manual,
            amount,
            currentCashSum,
            security);
}
