using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Migrations;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Migrations;

/// <summary>Migration batches R8, R9: the computed totals, the funding totals and one issue per cause.</summary>
public class MigrationBatchValidationTests
{
    private const int Smith = 5;
    private const int Jones = 6;
    private const int Vti = 9;
    private const int Bnd = 10;

    private static readonly Dictionary<int, MigrationAccountState> Accounts = new()
    {
        [Smith] = Account(Smith, "J Smith brokerage"),
        [Jones] = Account(Jones, "K Jones brokerage")
    };

    private static readonly Dictionary<int, MigrationInstrumentState> Instruments = new()
    {
        [Vti] = new MigrationInstrumentState(Vti, "VTI", true, "USD"),
        [Bnd] = new MigrationInstrumentState(Bnd, "BND", true, "USD")
    };

    private static MigrationAccountState Account(int id, string name, string currency = "USD") =>
        new(id, name, CustodyMode.Custodial, AccountStatus.Open, false, false, currency, 2);

    private static readonly IReadOnlyList<MigrationEntrySpec> Entries =
    [
        new(Smith, "ACME-1", 150000m, [new MigrationHoldingSpec(Vti, 800m, 176000m)]),
        new(Jones, "ACME-2", 100000m, [new MigrationHoldingSpec(Vti, 400m, 88000m), new MigrationHoldingSpec(Bnd, 10m, 700m)])
    ];

    private static readonly MigrationControlTotals Control = new(
        2,
        [new MigrationCurrencyAmount("USD", 250000m)],
        [new MigrationInstrumentQuantity(Vti, 1200m), new MigrationInstrumentQuantity(Bnd, 10m)],
        [new MigrationCurrencyAmount("USD", 264700m)]);

    private static MigrationValidationInputs Valid() =>
        new(Control, Entries, Accounts, Instruments, [new MigrationCurrencyAmount("USD", 250000m)]);

    private static IReadOnlyList<MigrationIssue> Issues(MigrationValidationInputs inputs) =>
        MigrationBatchValidation.Compute(inputs).Issues;

    [Test]
    public void AValidBatch_HasNoIssue()
    {
        var result = MigrationBatchValidation.Compute(Valid());

        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    [Test]
    public void ComputedTotals_ArePerAccountCurrency_PerInstrument_AndCostPerQuoteCurrency()
    {
        var accounts = new Dictionary<int, MigrationAccountState>(Accounts) { [Jones] = Account(Jones, "K Jones", "NZD") };
        var instruments = new Dictionary<int, MigrationInstrumentState>(Instruments)
        {
            [Bnd] = new MigrationInstrumentState(Bnd, "BND", true, "NZD")
        };

        var result = MigrationBatchValidation.Compute(Valid() with { Accounts = accounts, Instruments = instruments });

        result.ComputedTotals.AccountCount.ShouldBe(2);
        result.ComputedTotals.Cash.ShouldBe([new MigrationCurrencyAmount("USD", 150000m), new MigrationCurrencyAmount("NZD", 100000m)]);
        result.ComputedTotals.Quantity.ShouldBe([new MigrationInstrumentQuantity(Vti, 1200m), new MigrationInstrumentQuantity(Bnd, 10m)]);
        result.ComputedTotals.Cost.ShouldBe([new MigrationCurrencyAmount("USD", 264000m), new MigrationCurrencyAmount("NZD", 700m)]);
    }

    [Test]
    public void FundingTotals_AreTheSumOfTheLinesPerCurrency_WithEveryCashCurrency()
    {
        var result = MigrationBatchValidation.Compute(Valid() with
        {
            Funding = [new MigrationCurrencyAmount("USD", 200000m), new MigrationCurrencyAmount("USD", 50000m), new MigrationCurrencyAmount("AUD", 5m)]
        });

        result.FundingTotals.ShouldBe([new MigrationCurrencyAmount("USD", 250000m), new MigrationCurrencyAmount("AUD", 5m)]);
    }

    [Test]
    public void NoEntries_IsAnIssue()
    {
        var issues = Issues(new MigrationValidationInputs(MigrationControlTotals.Empty, [], Accounts, Instruments, []));

        issues.Select(i => i.Code).ShouldBe([MigrationIssueCode.NoEntries]);
        issues[0].Message.ShouldBe("The batch has no entries.");
    }

    private static IEnumerable<TestCaseData> AccountCases()
    {
        yield return new TestCaseData(Account(Smith, "J Smith brokerage") with { CustodyMode = CustodyMode.External },
            MigrationIssueCode.AccountNotCustodial,
            "J Smith brokerage is an External account. A migration batch fills Custodial accounts only.");
        yield return new TestCaseData(Account(Smith, "J Smith brokerage") with { Status = AccountStatus.Closed },
            MigrationIssueCode.AccountNotOpen, "J Smith brokerage is closed.");
        yield return new TestCaseData(Account(Smith, "J Smith brokerage") with { CustomerDisabled = true },
            MigrationIssueCode.CustomerDisabled, "The customer of J Smith brokerage is disabled.");
        yield return new TestCaseData(Account(Smith, "J Smith brokerage") with { HasTransactions = true },
            MigrationIssueCode.AccountNotEmpty,
            "J Smith brokerage already has transactions. A migration batch fills empty accounts only.");
        yield return new TestCaseData(Account(Smith, "J Smith brokerage") with { CurrencyDecimalPlaces = 0 },
            MigrationIssueCode.CashPrecision, "The cash of J Smith brokerage has more than 0 decimal places for USD.");
    }

    [TestCaseSource(nameof(AccountCases))]
    public void AnAccountState_IsOneIssueAboutTheEntry(MigrationAccountState state, MigrationIssueCode code, string message)
    {
        var entries = Entries.ToList();
        if (code == MigrationIssueCode.CashPrecision)
        {
            entries[0] = entries[0] with { Cash = 150000.5m };
        }

        var cash = code == MigrationIssueCode.CashPrecision ? 250000.5m : 250000m;
        var issues = Issues(Valid() with
        {
            Entries = entries,
            Accounts = new Dictionary<int, MigrationAccountState>(Accounts) { [Smith] = state },
            ControlTotals = Control with { Cash = [new MigrationCurrencyAmount("USD", cash)] },
            Funding = [new MigrationCurrencyAmount("USD", cash)]
        });

        issues.ShouldHaveSingleItem();
        (issues[0].Code, issues[0].AccountId, issues[0].InstrumentId, issues[0].Message).ShouldBe((code, (int?)Smith, (int?)null, message));
    }

    [Test]
    public void ADisabledInstrument_IsAnIssueAboutTheHolding()
    {
        var issues = Issues(Valid() with
        {
            Instruments = new Dictionary<int, MigrationInstrumentState>(Instruments)
            {
                [Bnd] = new MigrationInstrumentState(Bnd, "BND", false, "USD")
            }
        });

        issues.ShouldHaveSingleItem();
        (issues[0].Code, issues[0].AccountId, issues[0].InstrumentId, issues[0].Message)
            .ShouldBe((MigrationIssueCode.InstrumentInactive, (int?)Jones, (int?)Bnd, "BND is disabled."));
    }

    [Test]
    public void AnInstrumentInAnotherCurrency_IsAnIssueAboutTheHolding()
    {
        var issues = Issues(Valid() with
        {
            Instruments = new Dictionary<int, MigrationInstrumentState>(Instruments)
            {
                [Bnd] = new MigrationInstrumentState(Bnd, "BND", true, "NZD")
            },
            ControlTotals = Control with { Cost = [new MigrationCurrencyAmount("USD", 264000m), new MigrationCurrencyAmount("NZD", 700m)] }
        });

        issues.ShouldHaveSingleItem();
        (issues[0].Code, issues[0].AccountId, issues[0].InstrumentId, issues[0].Message)
            .ShouldBe((MigrationIssueCode.InstrumentCurrencyMismatch, (int?)Jones, (int?)Bnd, "BND is quoted in NZD; K Jones brokerage is in USD."));
    }

    [Test]
    public void AWrongAccountCount_IsOneControlTotalIssue()
    {
        var issue = Issues(Valid() with { ControlTotals = Control with { AccountCount = 3 } }).ShouldHaveSingleItem();

        (issue.Code, issue.Total, issue.Message).ShouldBe((MigrationIssueCode.ControlTotalMismatch, (MigrationTotal?)MigrationTotal.AccountCount,
            "The control total of accounts is 3; the batch has 2 entries."));
    }

    [Test]
    public void ADifferentCashTotal_IsAnIssue_AboutItsCurrency()
    {
        var issues = Issues(Valid() with { ControlTotals = Control with { Cash = [new MigrationCurrencyAmount("USD", 250000.01m)] } });

        var issue = issues.ShouldHaveSingleItem();
        (issue.Code, issue.Total, issue.Currency, issue.Message).ShouldBe((MigrationIssueCode.ControlTotalMismatch,
            (MigrationTotal?)MigrationTotal.Cash, "USD", "The control total of cash in USD is 250,000.01; the entries add up to 250,000.00."));
    }

    [Test]
    public void AMissingQuantityTotal_IsAnIssue_AboutItsInstrument()
    {
        var issue = Issues(Valid() with { ControlTotals = Control with { Quantity = [new MigrationInstrumentQuantity(Vti, 1200m)] } })
            .ShouldHaveSingleItem();

        (issue.Code, issue.Total, issue.InstrumentId, issue.Message).ShouldBe((MigrationIssueCode.ControlTotalMismatch,
            (MigrationTotal?)MigrationTotal.Quantity, (int?)Bnd, "BND has no control total; the entries hold 10."));
    }

    [Test]
    public void AnExtraControlTotalAbove0_IsAnIssue_AndAt0_IsNot()
    {
        var above = Issues(Valid() with
        {
            ControlTotals = Control with { Cost = [new MigrationCurrencyAmount("USD", 264700m), new MigrationCurrencyAmount("NZD", 1m)] }
        }).ShouldHaveSingleItem();
        (above.Code, above.Total, above.Currency, above.Message).ShouldBe((MigrationIssueCode.ControlTotalMismatch,
            (MigrationTotal?)MigrationTotal.Cost, "NZD", "The control total of cost in NZD is 1.00; the entries have none."));

        Issues(Valid() with
        {
            ControlTotals = Control with { Cost = [new MigrationCurrencyAmount("USD", 264700m), new MigrationCurrencyAmount("NZD", 0m)] }
        }).ShouldBeEmpty();
    }

    [Test]
    public void ShortFunding_IsAFundingIssue_WithTheR29Sentence()
    {
        var issue = Issues(Valid() with { Funding = [] }).ShouldHaveSingleItem();

        (issue.Code, issue.Currency, issue.Message).ShouldBe((MigrationIssueCode.FundingMismatch, "USD",
            "Bank receipts for this batch add up to 0.00 USD; its cash is 250,000.00 USD."));
    }

    [Test]
    public void OverFunding_IsAFundingIssue()
    {
        Issues(Valid() with { Funding = [new MigrationCurrencyAmount("USD", 250000.01m)] })
            .ShouldHaveSingleItem().Code.ShouldBe(MigrationIssueCode.FundingMismatch);
    }

    [Test]
    public void FundingInACurrencyWithNoCash_IsAFundingIssue()
    {
        var issue = Issues(Valid() with
        {
            Funding = [new MigrationCurrencyAmount("USD", 250000m), new MigrationCurrencyAmount("NZD", 10m)]
        }).ShouldHaveSingleItem();

        (issue.Code, issue.Currency, issue.Message).ShouldBe((MigrationIssueCode.FundingMismatch, "NZD",
            "Bank receipts for this batch add up to 10.00 NZD; its cash is 0.00 NZD."));
    }

    [Test]
    public void ABatchWithHoldingsOnly_NeedsNoFunding()
    {
        var entries = Entries.Select(e => e with { Cash = null }).ToList();

        Issues(Valid() with { Entries = entries, ControlTotals = Control with { Cash = [] }, Funding = [] }).ShouldBeEmpty();
    }

    [Test]
    public void Issues_ComeInTheOrderOfTheRule()
    {
        var issues = Issues(Valid() with
        {
            Accounts = new Dictionary<int, MigrationAccountState>(Accounts)
            {
                [Smith] = Account(Smith, "J Smith brokerage") with { HasTransactions = true, CustodyMode = CustodyMode.External }
            },
            Funding = [],
            ControlTotals = Control with { AccountCount = 1 }
        });

        issues.Select(i => i.Code).ShouldBe([
            MigrationIssueCode.AccountNotCustodial, MigrationIssueCode.AccountNotEmpty,
            MigrationIssueCode.ControlTotalMismatch, MigrationIssueCode.FundingMismatch
        ]);
    }
}
