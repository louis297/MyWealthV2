using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Tax;
using NUnit.Framework;
using Shouldly;
using static MyWealthV2.Domain.UnitTests.Tax.IncomeWorkedExample;
using static MyWealthV2.Domain.UnitTests.Tax.TaxWorkedExample;

namespace MyWealthV2.Domain.UnitTests.Tax;

/// <summary>Income-summary R3–R5: the interest items of a tax year, in NZD.</summary>
public class CustomerTaxYearInterestTests
{
    [Test]
    public void WorkedExample_2026_HasTheFourItemsOfTheExample_ToTheCent()
    {
        var items = IncomeWorkedExample.Year(2026).Interest;

        items.Select(i => (i.Sequence, i.Item.TransactionId, i.Status, i.Amount, i.Rwt, i.Nrwt, i.ForeignTax)).ShouldBe([
            (1, 101, TaxAmountStatus.Stated, 120.00m, 21.00m, 0m, 0m),
            (2, 102, TaxAmountStatus.Stated, -8.00m, 0m, 0m, 0m),
            (3, 104, TaxAmountStatus.Converted, 7.05m, 2.32m, 0m, 0m),
            (4, 105, TaxAmountStatus.Converted, 6.65m, 2.19m, 0m, 0m)
        ]);
        items.Select(i => i.Fx).ShouldBe([null, null, Rate(2026, 1, 30, 1.7200m), Rate(2026, 2, 27, 1.7500m)]);
    }

    [Test]
    public void TheInterestOfMarch_PostedOnTheFirstOfApril_IsInTheNextTaxYear()
    {
        var item = IncomeWorkedExample.Year(2027).Interest.ShouldHaveSingleItem();

        (item.Sequence, item.Item.TransactionId, item.Item.Period!.PeriodFrom).ShouldBe((1, 107, new DateOnly(2026, 3, 1)));
        (item.Status, item.Amount, item.Rwt, item.Fx).ShouldBe((TaxAmountStatus.Converted, 7.50m, 2.48m, Rate(2026, 3, 31, 1.7450m)));
    }

    [Test]
    public void AnItemAt1059AndOneAt1100UtcOn31March_FallInTwoTaxYears()
    {
        var before = Posting(201, Utc(2026, 3, 31, 10, 59), 1.00m, 0m, null);
        var after = Posting(202, Utc(2026, 3, 31, 11, 0), 2.00m, 0m, null);

        IncomeWorkedExample.Year(2026, [before, after]).Interest.Select(i => i.Item.TransactionId).ShouldBe([201]);
        IncomeWorkedExample.Year(2027, [before, after]).Interest.Select(i => i.Item.TransactionId).ShouldBe([202]);
    }

    [Test]
    public void AnNzdLeg_IsUsedAsBooked_AndNoRateIsRead()
    {
        var item = IncomeWorkedExample.Year(2026, [I1], []).Interest.ShouldHaveSingleItem();

        (item.Status, item.Amount, item.Rwt, item.Fx).ShouldBe((TaxAmountStatus.Stated, 120.00m, 21.00m, null));
    }

    [Test]
    public void EachFigure_IsConvertedOnItsOwn()
    {
        // 10.01 × 1.5 = 15.015 → 15.02 and 3.33 × 1.5 = 4.995 → 5.00, each rounded on its own.
        var item = IncomeWorkedExample.Year(2026, [Posting(203, Utc(2025, 6, 2, 0, 0), 10.01m, 3.33m, null)], [Rate(2025, 6, 2, 1.5000m)])
            .Interest.ShouldHaveSingleItem();

        (item.Amount, item.Rwt).ShouldBe((15.02m, 5.00m));
    }

    [Test]
    public void TheRateOfASunday_IsFridays()
    {
        var item = IncomeWorkedExample.Year(2026, [I3]).Interest.ShouldHaveSingleItem();

        I3.BookedAt.DayOfWeek.ShouldBe(DayOfWeek.Sunday);
        item.Fx.ShouldBe(Rate(2026, 1, 30, 1.7200m));
    }

    [Test]
    public void NoRate_LeavesEveryFigureNull()
    {
        var item = IncomeWorkedExample.Year(2026, [I3], []).Interest.ShouldHaveSingleItem();

        (item.Status, item.Amount, item.Rwt, item.Nrwt, item.ForeignTax, item.Fx).ShouldBe((TaxAmountStatus.NoRate, null, null, null, null, null));
    }

    [Test]
    public void ARowOlderThanFourDays_IsStale_AndLeavesEveryFigureNull()
    {
        var row = Rate(2026, 1, 27, 1.7000m);

        var item = IncomeWorkedExample.Year(2026, [I3], [row]).Interest.ShouldHaveSingleItem();

        (item.Status, item.Amount, item.Rwt, item.Nrwt, item.ForeignTax, item.Fx).ShouldBe((TaxAmountStatus.StaleRate, null, null, null, null, row));
    }

    [Test]
    public void WithNoInterestGiven_TheYearHasNoItem() =>
        TaxWorkedExample.Year(2026).Interest.ShouldBeEmpty();
}
