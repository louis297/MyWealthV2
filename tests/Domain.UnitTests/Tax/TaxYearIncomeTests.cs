using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Tax;
using MyWealthV2.Domain.Valuations;
using NUnit.Framework;
using Shouldly;
using static MyWealthV2.Domain.UnitTests.Tax.IncomeWorkedExample;
using static MyWealthV2.Domain.UnitTests.Tax.TaxWorkedExample;

namespace MyWealthV2.Domain.UnitTests.Tax;

/// <summary>Income-summary R6–R12 and the invariants of §5: the groups, the sums, a missing figure, the reasons and the
/// notices of the income summary.</summary>
public class TaxYearIncomeTests
{
    private static IncomeDividendSum Dividends(int count, decimal gross, decimal credit, decimal foreignTax, decimal rwt, decimal nrwt = 0m) =>
        new(count, gross, credit, gross + credit, foreignTax, rwt, nrwt);

    private static readonly IncomeDividendSum NoDividend = new(0, 0m, 0m, 0m, 0m, 0m, 0m);

    private static readonly IncomeDividendSum MissingDividend = new(1, null, null, null, null, null, null);

    private static IncomeDividendGroup Group(TaxYearIncome income, IncomeGroup group) =>
        income.Dividends.Groups.Single(g => g.Group == group);

    private static TaxYearIncome Income(
        int taxYear,
        IReadOnlyList<LedgerFact>? facts = null,
        IReadOnlyList<StoredRate>? rates = null,
        IReadOnlyDictionary<int, TaxTreatment>? treatments = null) =>
        IncomeWorkedExample.Year(taxYear, facts, rates, treatments).Income;

    [Test]
    public void WorkedExample_2026_HasEverySumOfTheExample()
    {
        var income = Income(2026);

        income.Dividends.Groups.Select(g => g.Group).ShouldBe(
            [IncomeGroup.NewZealand, IncomeGroup.ForeignExempt, IncomeGroup.Attributing, IncomeGroup.NotClassified]);

        var newZealand = Group(income, IncomeGroup.NewZealand);
        newZealand.Holdings.ShouldBe([new IncomeHoldingRow(Kiw, "NZ", Dividends(1, 720.00m, 280.00m, 0m, 50.00m))]);
        newZealand.Countries.ShouldBeEmpty();
        newZealand.Total.ShouldBe(Dividends(1, 720.00m, 280.00m, 0m, 50.00m));
        newZealand.Total.GrossWithImputation.ShouldBe(1_000.00m);

        var exempt = Group(income, IncomeGroup.ForeignExempt);
        exempt.Holdings.ShouldBe([new IncomeHoldingRow(Ozb, "AU", Dividends(1, 327.00m, 0m, 49.05m, 0m))]);
        exempt.Countries.ShouldBe([new IncomeCountryRow("AU", Dividends(1, 327.00m, 0m, 49.05m, 0m))]);
        exempt.Total.ShouldBe(Dividends(1, 327.00m, 0m, 49.05m, 0m));

        var attributing = Group(income, IncomeGroup.Attributing);
        attributing.Holdings.ShouldBe([new IncomeHoldingRow(Aaa, "US", Dividends(2, 117.95m, 0m, 17.69m, 21.23m))]);
        attributing.Countries.ShouldBe([new IncomeCountryRow("US", Dividends(2, 117.95m, 0m, 17.69m, 21.23m))]);

        var notClassified = Group(income, IncomeGroup.NotClassified);
        (notClassified.Holdings.Count, notClassified.Countries.Count, notClassified.Total).ShouldBe((0, 0, NoDividend));

        income.Dividends.Total.ShouldBe(Dividends(4, 1_164.95m, 280.00m, 66.74m, 71.23m));
        income.Dividends.Total.GrossWithImputation.ShouldBe(1_444.95m);

        income.Interest.Accounts.ShouldBe([
            new IncomeAccountRow(A2, CustodyMode.External, "NZD", new IncomeInterestSum(2, 120.00m, 8.00m, 21.00m, 0m, 0m)),
            new IncomeAccountRow(A1, CustodyMode.Custodial, "USD", new IncomeInterestSum(2, 13.70m, 0m, 4.51m, 0m, 0m))
        ]);
        income.Interest.Total.ShouldBe(new IncomeInterestSum(4, 133.70m, 8.00m, 25.51m, 0m, 0m));

        income.TaxWithheld.ShouldBe(new IncomeTaxWithheld(96.74m, 0m, 66.74m));
        income.ImputationCredit.ShouldBe(280.00m);
        income.Complete.ShouldBeTrue();
        income.Incomplete.ShouldBeEmpty();
        income.Notices.ShouldBe([
            new IncomeNoticeLine(IncomeNotice.ExternalFigures, null, A2),
            new IncomeNoticeLine(IncomeNotice.InterestCharged, null, A2)
        ]);
    }

    [TestCase(FifTreatment.NotForeign, IncomeGroup.NewZealand)]
    [TestCase(FifTreatment.ExemptAsxListed, IncomeGroup.ForeignExempt)]
    [TestCase(FifTreatment.ExemptOther, IncomeGroup.ForeignExempt)]
    [TestCase(FifTreatment.Attributing, IncomeGroup.Attributing)]
    [TestCase(FifTreatment.AttributingNoFdr, IncomeGroup.Attributing)]
    [TestCase(FifTreatment.NotClassified, IncomeGroup.NotClassified)]
    public void EachTreatment_FallsInItsGroup_AndTheFourGroupsAreAlwaysGiven(FifTreatment treatment, IncomeGroup group)
    {
        var treatments = treatment == FifTreatment.NotClassified
            ? new Dictionary<int, TaxTreatment>()
            : new Dictionary<int, TaxTreatment> { [Kiw] = new(treatment, 2026, null, "NZ") };

        var income = Income(2026, [D1], treatments: treatments);

        income.Dividends.Groups.Select(g => g.Group).ShouldBe(
            [IncomeGroup.NewZealand, IncomeGroup.ForeignExempt, IncomeGroup.Attributing, IncomeGroup.NotClassified]);
        income.Dividends.Groups.Where(g => g.Holdings.Count > 0).Select(g => g.Group).ShouldBe([group]);
        Group(income, group).Holdings.ShouldHaveSingleItem().InstrumentId.ShouldBe(Kiw);
    }

    [Test]
    public void ADividendWhoseTaxSourceDisagrees_StaysInTheGroupOfItsHolding_WithANotice()
    {
        var treatments = new Dictionary<int, TaxTreatment> { [Kiw] = new(FifTreatment.NotForeign, 2025, null, "AU") };

        var income = Income(2026, [D1], treatments: treatments);

        Group(income, IncomeGroup.NewZealand).Holdings.ShouldHaveSingleItem().InstrumentId.ShouldBe(Kiw);
        income.Notices.ShouldContain(new IncomeNoticeLine(IncomeNotice.DividendTaxSourceDisagrees, Kiw, null));
        income.Complete.ShouldBeTrue();
    }

    [Test]
    public void TheTwoForeignGroups_HaveARowPerCountry_WithNoCountryLast()
    {
        var treatments = new Dictionary<int, TaxTreatment>
        {
            [Aaa] = new(FifTreatment.ExemptOther, 2026, "Note", "US"),
            [Ozb] = new(FifTreatment.ExemptAsxListed, 2026, null, null)
        };

        var exempt = Group(Income(2026, [D2, T4], treatments: treatments), IncomeGroup.ForeignExempt);

        exempt.Holdings.Select(h => (h.InstrumentId, h.IssuerCountry)).ShouldBe([(Aaa, "US"), (Ozb, null)]);
        exempt.Countries.ShouldBe([
            new IncomeCountryRow("US", Dividends(1, 59.15m, 0m, 8.87m, 10.65m)),
            new IncomeCountryRow(null, Dividends(1, 327.00m, 0m, 49.05m, 0m))
        ]);
    }

    [Test]
    public void TwoHoldingsOfOneCountry_AreOneCountryRow()
    {
        var treatments = new Dictionary<int, TaxTreatment>
        {
            [Aaa] = new(FifTreatment.Attributing, 2025, null, "AU"),
            [Ozb] = new(FifTreatment.AttributingNoFdr, 2026, null, "AU")
        };

        var attributing = Group(Income(2026, [D2, T4], treatments: treatments), IncomeGroup.Attributing);

        attributing.Countries.ShouldBe([new IncomeCountryRow("AU", Dividends(2, 386.15m, 0m, 57.92m, 10.65m))]);
        attributing.Total.ShouldBe(Dividends(2, 386.15m, 0m, 57.92m, 10.65m));
    }

    [Test]
    public void AReinvestedDividend_CountsOnce_AtItsGross()
    {
        var row = Group(Income(2026, [T1, T9]), IncomeGroup.Attributing).Holdings.ShouldHaveSingleItem();

        row.Sum.ShouldBe(Dividends(1, 58.80m, 0m, 8.82m, 10.58m));
    }

    [Test]
    public void InterestReceivedAndInterestCharged_AreNotNetted()
    {
        var total = Income(2026, [I1, I2]).Interest.Total;

        (total.Count, total.Received, total.Charged).ShouldBe((2, 120.00m, 8.00m));
    }

    [Test]
    public void AMissingFigureOfADividend_MakesEverySumItBelongsInNull_AndStillCounts()
    {
        var income = Income(2026, [T4, D1], rates: []);

        var attributing = Group(income, IncomeGroup.Attributing);
        attributing.Holdings.ShouldHaveSingleItem().Sum.ShouldBe(MissingDividend);
        attributing.Countries.ShouldBe([new IncomeCountryRow("US", MissingDividend)]);
        attributing.Total.ShouldBe(MissingDividend);
        Group(income, IncomeGroup.NewZealand).Total.ShouldBe(Dividends(1, 720.00m, 280.00m, 0m, 50.00m));
        income.Dividends.Total.ShouldBe(new IncomeDividendSum(2, null, null, null, null, null, null));
        income.TaxWithheld.ShouldBe(new IncomeTaxWithheld(null, null, null));
        income.ImputationCredit.ShouldBeNull();
        income.Complete.ShouldBeFalse();
        income.Incomplete.ShouldBe([new IncomeReason(IncomeIncompleteReason.AmountMissing, Aaa, null)]);
    }

    [Test]
    public void AMissingFigureOfAnInterestItem_MakesItsAccountAndTheTotalNull_AndStillCounts()
    {
        var income = Income(2026, [I1, I3, I4], rates: [Rate(2026, 2, 27, 1.7500m)]);

        income.Interest.Accounts.Select(a => (a.AccountId, a.Sum)).ShouldBe([
            (A2, new IncomeInterestSum(1, 120.00m, 0m, 21.00m, 0m, 0m)),
            (A1, new IncomeInterestSum(2, null, null, null, null, null))
        ]);
        income.Interest.Total.ShouldBe(new IncomeInterestSum(3, null, null, null, null, null));
        income.TaxWithheld.ShouldBe(new IncomeTaxWithheld(null, null, null));
        income.ImputationCredit.ShouldBe(0m);
        income.Complete.ShouldBeFalse();
        income.Incomplete.ShouldBe([new IncomeReason(IncomeIncompleteReason.AmountMissing, null, A1)]);
    }

    [Test]
    public void ANotClassifiedHolding_MakesTheSummaryIncomplete_OnlyWithADividendInTheYear()
    {
        var treatments = new Dictionary<int, TaxTreatment>();

        var withDividend = Income(2026, [T1, T4], treatments: treatments);
        var withoutDividend = Income(2026, [T1, T3], treatments: treatments);

        Group(withDividend, IncomeGroup.NotClassified).Total.ShouldBe(Dividends(1, 59.15m, 0m, 8.87m, 10.65m));
        withDividend.Incomplete.ShouldBe([new IncomeReason(IncomeIncompleteReason.NotClassified, Aaa, null)]);
        withDividend.Complete.ShouldBeFalse();
        withoutDividend.Incomplete.ShouldBeEmpty();
        withoutDividend.Complete.ShouldBeTrue();
    }

    [Test]
    public void AHoldingWithNoPrice_DoesNotMakeTheSummaryIncomplete()
    {
        var year = IncomeWorkedExample.Year(2026);

        year.Complete.ShouldBeFalse();
        year.Income.Complete.ShouldBeTrue();
    }

    [Test]
    public void ExternalFigures_IsOnePerAccount_OfADividendOrAnInterestItemOnAnExternalAccount()
    {
        Income(2026, [D1, D2, I1]).Notices.Where(n => n.Notice == IncomeNotice.ExternalFigures)
            .ShouldBe([new IncomeNoticeLine(IncomeNotice.ExternalFigures, null, A2)]);
        Income(2026, [T4, I3]).Notices.ShouldBeEmpty();
    }

    [Test]
    public void AScrip_IsANoticeOfItsHolding()
    {
        var scrip = Fact(140, Utc(2025, 9, 2, 0, 0), TransactionType.Scrip, [], [Security(140, A1, 4m, 0m, Kiw)]);

        var income = Income(2026, [scrip]);

        income.Notices.ShouldBe([new IncomeNoticeLine(IncomeNotice.Scrip, Kiw, null)]);
        income.Dividends.Total.ShouldBe(NoDividend);
        income.Complete.ShouldBeTrue();
    }

    [Test]
    public void InterestCharged_IsANoticeOfItsAccount() =>
        Income(2026, [I2]).Notices.ShouldBe([
            new IncomeNoticeLine(IncomeNotice.ExternalFigures, null, A2),
            new IncomeNoticeLine(IncomeNotice.InterestCharged, null, A2)
        ]);

    [Test]
    public void ATaxYearWithNothing_HasFourEmptyGroups_AndIsComplete()
    {
        var income = Income(2026, []);

        income.Dividends.Groups.Select(g => (g.Group, g.Holdings.Count, g.Countries.Count, g.Total)).ShouldBe([
            (IncomeGroup.NewZealand, 0, 0, NoDividend),
            (IncomeGroup.ForeignExempt, 0, 0, NoDividend),
            (IncomeGroup.Attributing, 0, 0, NoDividend),
            (IncomeGroup.NotClassified, 0, 0, NoDividend)
        ]);
        income.Dividends.Total.ShouldBe(NoDividend);
        income.Interest.Accounts.ShouldBeEmpty();
        income.Interest.Total.ShouldBe(new IncomeInterestSum(0, 0m, 0m, 0m, 0m, 0m));
        income.TaxWithheld.ShouldBe(new IncomeTaxWithheld(0m, 0m, 0m));
        (income.ImputationCredit, income.Complete, income.Incomplete.Count, income.Notices.Count).ShouldBe((0m, true, 0, 0));
    }

    [Test]
    public void TheInvariantsOfSection5_Hold()
    {
        foreach (var year in new[] { IncomeWorkedExample.Year(2026), IncomeWorkedExample.Year(2026, rates: []) })
        {
            var income = year.Income;
            var dividends = year.Holdings.SelectMany(h => h.Events).Count(e => e.Dividend is not null);

            income.Dividends.Groups.Sum(g => g.Holdings.Sum(h => h.Sum.Count)).ShouldBe(dividends);
            income.Dividends.Total.Count.ShouldBe(dividends);
            income.Interest.Accounts.Sum(a => a.Sum.Count).ShouldBe(year.Interest.Count);
            income.Interest.Total.Count.ShouldBe(year.Interest.Count);
            income.Complete.ShouldBe(income.Incomplete.Count == 0);

            foreach (var group in income.Dividends.Groups)
            {
                group.Total.ShouldBe(SumOf(group.Holdings.Select(h => h.Sum)));
            }

            income.Dividends.Total.ShouldBe(SumOf(income.Dividends.Groups.Select(g => g.Total)));
            foreach (var sum in income.Dividends.Groups.SelectMany(g => g.Holdings.Select(h => h.Sum).Append(g.Total)))
            {
                if (sum.Gross is { } gross && sum.ImputationCredit is { } credit)
                {
                    sum.GrossWithImputation.ShouldBe(gross + credit);
                }
            }

            foreach (var sum in income.Interest.Accounts.Select(a => a.Sum).Append(income.Interest.Total))
            {
                (sum.Received ?? 0m).ShouldBeGreaterThanOrEqualTo(0m);
                (sum.Charged ?? 0m).ShouldBeGreaterThanOrEqualTo(0m);
            }
        }
    }

    private static IncomeDividendSum SumOf(IEnumerable<IncomeDividendSum> sums)
    {
        var list = sums.ToList();
        decimal? Add(Func<IncomeDividendSum, decimal?> figure) =>
            list.Any(s => figure(s) is null) ? null : list.Sum(s => figure(s)!.Value);
        return new IncomeDividendSum(
            list.Sum(s => s.Count), Add(s => s.Gross), Add(s => s.ImputationCredit), Add(s => s.GrossWithImputation),
            Add(s => s.ForeignTax), Add(s => s.Rwt), Add(s => s.Nrwt));
    }
}
