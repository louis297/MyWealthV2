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
            // External income R21: a dividend an outside party paid, on POST /transactions/dividend.
            [TransactionType.Dividend] = true,
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
            [TransactionType.TransferIn] = false,
            [TransactionType.TransferOut] = false,
            [TransactionType.Interest] = false,
            [TransactionType.CloseOut] = false,
            [TransactionType.Opening] = false,
            [TransactionType.Reversal] = true,
            [TransactionType.Dividend] = false,
            // Orders R48: a trade on a Custodial account goes through the broker.
            [TransactionType.Buy] = false,
            [TransactionType.Sell] = false,
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

    [TestCase(TransactionType.TransferIn,
        "Deposits to a Custodial account come from the bank statement. The customer pays in with the account's deposit instructions.")]
    [TestCase(TransactionType.Interest, "Interest cannot be entered by hand on a Custodial account.")]
    [TestCase(TransactionType.Opening, "Opening balances on a Custodial account come from a migration batch.")]
    [TestCase(TransactionType.OpeningHoldings, "Opening balances on a Custodial account come from a migration batch.")]
    [TestCase(TransactionType.Buy, "Trades on a Custodial account go through the broker. Place an order.")]
    [TestCase(TransactionType.Sell, "Trades on a Custodial account go through the broker. Place an order.")]
    [TestCase(TransactionType.StockIn, "Securities move in or out of a Custodial account by an in-kind transfer. Request a transfer.")]
    [TestCase(TransactionType.StockOut, "Securities move in or out of a Custodial account by an in-kind transfer. Request a transfer.")]
    [TestCase(TransactionType.Split, "Corporate actions cannot be entered by hand on a Custodial account.")]
    [TestCase(TransactionType.Bonus, "Corporate actions cannot be entered by hand on a Custodial account.")]
    [TestCase(TransactionType.Scrip, "Corporate actions cannot be entered by hand on a Custodial account.")]
    [TestCase(TransactionType.Dividend, "Dividends are paid by the platform from the company's announcement.")]
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
    public void RefusalMessage_DividendOnAnExternalAccount_IsNull() =>
        CustodyRules.RefusalMessage(CustodyMode.External, TransactionType.Dividend).ShouldBeNull();

    [Test]
    public void RefusalMessage_AdjustmentOnACustodialAccount_IsNull()
    {
        CustodyRules.RefusalMessage(CustodyMode.Custodial, TransactionType.Adjustment).ShouldBeNull();
    }

    [TestCaseSource(nameof(Matrix))]
    public void RefusalMessage_IsNullExactlyWhenTheTypeIsAllowed(CustodyMode custodyMode, TransactionType type, bool allowed)
    {
        (CustodyRules.RefusalMessage(custodyMode, type) is null).ShouldBe(allowed);
    }

    [TestCase(CustodyMode.External, TransactionSourceKind.Manual, true)]
    [TestCase(CustodyMode.External, TransactionSourceKind.Seed, true)]
    [TestCase(CustodyMode.External, TransactionSourceKind.Rebalance, true)]
    [TestCase(CustodyMode.Custodial, TransactionSourceKind.Manual, true)]
    [TestCase(CustodyMode.Custodial, TransactionSourceKind.Seed, false)]
    // Rebalances R28: Rebalance leaves the hand-reversible sources on a Custodial account.
    [TestCase(CustodyMode.Custodial, TransactionSourceKind.Rebalance, false)]
    [TestCase(CustodyMode.Custodial, TransactionSourceKind.OrderFill, false)]
    [TestCase(CustodyMode.Custodial, TransactionSourceKind.BankFeed, false)]
    [TestCase(CustodyMode.Custodial, TransactionSourceKind.AccountClosure, false)]
    // Migration batches R25, R27: Migration is not hand-reversible.
    [TestCase(CustodyMode.Custodial, TransactionSourceKind.Migration, false)]
    // In-kind transfers R41, R49: InKindTransfer is not hand-reversible.
    [TestCase(CustodyMode.Custodial, TransactionSourceKind.InKindTransfer, false)]
    // Adjustment review R13: AdjustmentRequest is not hand-reversible.
    [TestCase(CustodyMode.Custodial, TransactionSourceKind.AdjustmentRequest, false)]
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

    [Test]
    public void TransferOut_OnACustodialAccount_IsRefused_WithTheWithdrawalsMessage() =>
        CustodyRules.RefusalMessage(CustodyMode.Custodial, TransactionType.TransferOut).ShouldBe(
            "Withdrawals from a Custodial account are paid to the customer's nominated bank account. Request a withdrawal.");

    private const string CloseOutMessage =
        "The remainder of a Custodial account is written off by its account closure. Request an account closure.";

    [TestCase(0.004, "USD", 2)]
    [TestCase(0.0099, "USD", 2)]
    [TestCase(0.01, "USD", 2)]
    [TestCase(5.00, "USD", 2)]
    [TestCase(0, "USD", 2)]
    [TestCase(0.4, "JPY", 0)]
    public void CloseOut_OnACustodialAccount_IsRefusedForEveryAmount_WithTheAccountClosureMessage(
        decimal sum, string currency, int places) =>
        CustodyRules.CloseOutRefusal(CustodyMode.Custodial, sum, currency, places).ShouldBe(CloseOutMessage);

    [Test]
    public void CloseOut_OnACustodialAccount_IsARefusedCellOfTheMatrix() =>
        CustodyRules.RefusalMessage(CustodyMode.Custodial, TransactionType.CloseOut).ShouldBe(CloseOutMessage);

    [TestCase(5)]
    [TestCase(0.004)]
    public void CloseOut_OnAnExternalAccount_IsNotLimited(decimal sum) =>
        CustodyRules.CloseOutRefusal(CustodyMode.External, sum, "USD", 2).ShouldBeNull();

    [TestCase(CustodyMode.Custodial, 1, 1, true)]
    [TestCase(CustodyMode.Custodial, 1, 2, false)]
    [TestCase(CustodyMode.External, 1, 2, true)]
    public void InternalTransfer_BetweenCustodialAccounts_StaysWithOneCustomer(CustodyMode mode, int from, int to, bool allowed) =>
        CustodyRules.AllowsCustodialTransfer(mode, from, to).ShouldBe(allowed);
}
