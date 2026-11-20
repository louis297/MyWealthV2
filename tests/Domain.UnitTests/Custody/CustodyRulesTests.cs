using MyWealthV2.Domain.Custody;
using MyWealthV2.Domain.Enums;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Custody;

/// <summary>Custody modes R5: the manual entry matrix, one case per (custody mode, type) pair.</summary>
public class CustodyRulesTests
{
    private static IEnumerable<TestCaseData> Matrix()
    {
        var external = new Dictionary<TransactionType, bool>
        {
            [TransactionType.TransferIn] = true,
            [TransactionType.TransferOut] = true,
            [TransactionType.Interest] = true,
            [TransactionType.CloseOut] = true,
            [TransactionType.Opening] = true,
            [TransactionType.Reversal] = true,
            [TransactionType.Dividend] = false,
            [TransactionType.Buy] = true,
            [TransactionType.Sell] = true,
            [TransactionType.OpeningHoldings] = true,
            [TransactionType.StockIn] = true,
            [TransactionType.StockOut] = true,
            [TransactionType.Split] = true,
            [TransactionType.Bonus] = true,
            [TransactionType.Scrip] = true,
            [TransactionType.InternalTransfer] = true,
            [TransactionType.FxConversion] = true,
            [TransactionType.Adjustment] = false
        };
        var custodial = new Dictionary<TransactionType, bool>
        {
            [TransactionType.TransferIn] = true,
            [TransactionType.TransferOut] = true,
            [TransactionType.Interest] = false,
            [TransactionType.CloseOut] = true,
            [TransactionType.Opening] = false,
            [TransactionType.Reversal] = true,
            [TransactionType.Dividend] = false,
            [TransactionType.Buy] = true,
            [TransactionType.Sell] = true,
            [TransactionType.OpeningHoldings] = false,
            [TransactionType.StockIn] = false,
            [TransactionType.StockOut] = false,
            [TransactionType.Split] = false,
            [TransactionType.Bonus] = false,
            [TransactionType.Scrip] = false,
            [TransactionType.InternalTransfer] = true,
            [TransactionType.FxConversion] = true,
            [TransactionType.Adjustment] = true
        };

        external.Count.ShouldBe(Enum.GetValues<TransactionType>().Length);
        custodial.Count.ShouldBe(Enum.GetValues<TransactionType>().Length);
        foreach (var (type, allowed) in external)
        {
            yield return new TestCaseData(CustodyMode.External, type, allowed);
        }

        foreach (var (type, allowed) in custodial)
        {
            yield return new TestCaseData(CustodyMode.Custodial, type, allowed);
        }
    }

    [TestCaseSource(nameof(Matrix))]
    public void AllowsManualEntry_MatchesTheMatrix(CustodyMode custodyMode, TransactionType type, bool allowed)
    {
        CustodyRules.AllowsManualEntry(custodyMode, type).ShouldBe(allowed);
    }

    [TestCase(TransactionType.Interest, "Interest cannot be entered by hand on a Custodial account.")]
    [TestCase(TransactionType.Opening, "A Custodial account starts empty. Fund it with TransferIn.")]
    [TestCase(TransactionType.OpeningHoldings, "A Custodial account starts empty. Fund it with TransferIn.")]
    [TestCase(TransactionType.StockIn, "Securities cannot be moved in or out of a Custodial account by hand. Use Buy and Sell.")]
    [TestCase(TransactionType.StockOut, "Securities cannot be moved in or out of a Custodial account by hand. Use Buy and Sell.")]
    [TestCase(TransactionType.Split, "Corporate actions cannot be entered by hand on a Custodial account.")]
    [TestCase(TransactionType.Bonus, "Corporate actions cannot be entered by hand on a Custodial account.")]
    [TestCase(TransactionType.Scrip, "Corporate actions cannot be entered by hand on a Custodial account.")]
    public void RefusalMessage_OnACustodialAccount_IsTheSentenceOfTheRule(TransactionType type, string message)
    {
        CustodyRules.RefusalMessage(CustodyMode.Custodial, type).ShouldBe(message);
    }

    [Test]
    public void RefusalMessage_AdjustmentOnAnExternalAccount_IsTheSentenceOfAdjustmentsR5()
    {
        CustodyRules.RefusalMessage(CustodyMode.External, TransactionType.Adjustment)
            .ShouldBe("An adjustment can be entered only on a Custodial account.");
    }

    [Test]
    public void RefusalMessage_AdjustmentOnACustodialAccount_IsNull()
    {
        CustodyRules.RefusalMessage(CustodyMode.Custodial, TransactionType.Adjustment).ShouldBeNull();
    }

    [TestCaseSource(nameof(Matrix))]
    public void RefusalMessage_IsNullExactlyWhenTheTypeIsAllowed(CustodyMode custodyMode, TransactionType type, bool allowed)
    {
        if (type == TransactionType.Dividend)
        {
            Assert.Pass("Dividend is reserved; no create route accepts it.");
        }

        (CustodyRules.RefusalMessage(custodyMode, type) is null).ShouldBe(allowed);
    }

    [TestCase(CustodyMode.External, TransactionSourceKind.Manual, true)]
    [TestCase(CustodyMode.External, TransactionSourceKind.Seed, true)]
    [TestCase(CustodyMode.External, TransactionSourceKind.Rebalance, true)]
    [TestCase(CustodyMode.Custodial, TransactionSourceKind.Manual, true)]
    [TestCase(CustodyMode.Custodial, TransactionSourceKind.Seed, false)]
    [TestCase(CustodyMode.Custodial, TransactionSourceKind.Rebalance, true)]
    [TestCase(CustodyMode.Custodial, TransactionSourceKind.BankFeed, false)]
    public void AllowsHandReverse_MatchesTransactionSourceR12ToR15(
        CustodyMode custodyMode,
        TransactionSourceKind sourceKind,
        bool allowed)
    {
        CustodyRules.AllowsHandReverse(custodyMode, sourceKind).ShouldBe(allowed);
    }

    [Test]
    public void AllowsHandReverse_EveryKindOnExternal_IsAllowed()
    {
        Enum.GetValues<TransactionSourceKind>()
            .ShouldAllBe(kind => CustodyRules.AllowsHandReverse(CustodyMode.External, kind));
    }
}
