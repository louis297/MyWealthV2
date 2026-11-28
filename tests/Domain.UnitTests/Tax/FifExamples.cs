using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Tax;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Tax;

/// <summary>
/// Tax holdings for the examples of docs/features/fif-income.md §1.1, built as the tax year read gives them: the
/// opening and closing value in NZD, and the events of the year with their NZD amount and the comparable quantity
/// after each. A step's <see cref="Step.Shares"/> is the change of the comparable quantity; <see cref="Step.Booked"/>
/// the change as booked, which differs only across a reorganisation.
/// </summary>
internal static class FifExamples
{
    public const int Qqq = 1;
    public const int Rrr = 2;
    public const int Sss = 3;

    public sealed record Step(
        TaxEventKind Kind,
        decimal Shares,
        decimal? Amount = null,
        decimal ImputationCredit = 0m,
        decimal? Booked = null,
        int AccountId = 1,
        decimal? Principal = null,
        decimal? Fee = null,
        decimal ForeignTax = 0m,
        decimal Rwt = 0m);

    public static Step Buy(decimal shares, decimal amount, int accountId = 1) => new(TaxEventKind.Acquisition, shares, amount, AccountId: accountId);

    public static Step Sell(decimal shares, decimal amount, int accountId = 1) => new(TaxEventKind.Disposal, -shares, amount, AccountId: accountId);

    public static Step Arrive(decimal shares, decimal value) => new(TaxEventKind.Arrival, shares, value);

    public static Step Depart(decimal shares, decimal value) => new(TaxEventKind.Departure, -shares, value);

    public static Step Dividend(decimal gross, decimal imputationCredit = 0m, decimal foreignTax = 0m, decimal rwt = 0m) =>
        new(TaxEventKind.Dividend, 0m, gross, imputationCredit, ForeignTax: foreignTax, Rwt: rwt);

    /// <summary>An acquisition whose amount is the principal and the fee (tax-year-events R10).</summary>
    public static Step BuyWithFee(decimal shares, decimal principal, decimal fee) =>
        new(TaxEventKind.Acquisition, shares, principal + fee, Principal: principal, Fee: fee);

    /// <summary>A disposal whose amount is the principal less the fee (tax-year-events R10).</summary>
    public static Step SellWithFee(decimal shares, decimal principal, decimal fee) =>
        new(TaxEventKind.Disposal, -shares, principal - fee, Principal: principal, Fee: fee);

    public static Step Split(decimal bookedChange) => new(TaxEventKind.Reorganisation, 0m, Booked: bookedChange);

    public static Step Move() => new(TaxEventKind.Move, 0m);

    public static Step Correct(decimal shares) => new(TaxEventKind.Correction, shares);

    /// <summary>One tax holding of <paramref name="taxYear"/>. <paramref name="opening"/> is the comparable quantity at
    /// the opening, <paramref name="openingBooked"/> the quantity as booked (the same unless given).</summary>
    public static TaxHolding Holding(
        decimal opening,
        decimal? openingValue,
        decimal? closingValue,
        IReadOnlyList<Step> steps,
        int instrumentId = Qqq,
        FifTreatment treatment = FifTreatment.Attributing,
        IReadOnlyList<TaxIncompleteReason>? incomplete = null,
        decimal? openingBooked = null,
        int taxYear = 2026)
    {
        var needsValue = CustomerTaxYear.NeedsValue(treatment);
        var comparable = opening;
        var booked = openingBooked ?? opening;
        var start = TaxYear.Start(taxYear);
        var events = new List<TaxYearEvent>();
        foreach (var step in steps)
        {
            var sequence = events.Count + 1;
            var change = step.Booked ?? step.Shares;
            comparable += step.Shares;
            booked += change;
            var taxEvent = new TaxEvent(
                sequence - 1,
                step.Kind,
                TransactionType.Buy,
                sequence,
                TaxWorkedExample.IdOf(sequence),
                start.AddDays(sequence),
                instrumentId,
                step.AccountId,
                CustodyMode.External,
                change,
                [new TaxQuantityChange(step.AccountId, CustodyMode.External, change)]);
            var dividend = step.Kind == TaxEventKind.Dividend
                ? new TaxDividendAmounts(booked, DividendTaxSource.Foreign, step.Amount, step.ForeignTax, step.Rwt, 0m, step.ImputationCredit)
                : null;
            events.Add(new TaxYearEvent(
                sequence, taxEvent, booked, comparable, step.Amount, TaxAmountStatus.Stated, null, null, step.Principal, step.Fee, dividend, null));
        }

        var peak = events.Select(e => e.ComparableQuantityAfter!.Value).Append(opening).Max();
        return new TaxHolding(
            instrumentId,
            new TaxTreatment(treatment, 2009, null, "US"),
            Point(TaxYear.Start(taxYear), TaxYear.OpeningDay(taxYear), openingBooked ?? opening, opening, openingValue, needsValue),
            Point(TaxYear.End(taxYear), TaxYear.ClosingDay(taxYear), booked, comparable, closingValue, needsValue),
            peak,
            events,
            incomplete ?? [],
            [],
            TaxCost.NotRequired);
    }

    private static TaxPoint Point(DateTimeOffset at, DateOnly day, decimal quantity, decimal comparable, decimal? value, bool needsValue) =>
        new(
            at,
            day,
            quantity,
            comparable,
            needsValue ? value : null,
            !needsValue ? TaxValueStatus.NotRequired : value is null ? TaxValueStatus.NotValued : TaxValueStatus.Valued,
            []);

    /// <summary>The income of one tax holding, computed with NZD's 2 places.</summary>
    public static TaxHoldingIncome IncomeOf(TaxHolding holding, int taxYear = 2026) =>
        Compute([holding], taxYear).Holdings.ShouldHaveSingleItem();

    public static FifIncome Compute(IReadOnlyList<TaxHolding> holdings, int taxYear = 2026) => FifIncome.Compute(taxYear, holdings, 2);

    /// <summary>Example 2 of §1.1: <c>QQQ</c> across a 2 for 1 split, every figure in NZD as booked.</summary>
    public static TaxHolding Example2(int instrumentId = Qqq) =>
        Holding(2_000m, 20_000.00m, 23_100.00m,
        [
            new Step(TaxEventKind.Acquisition, 800m, 8_800.00m, Booked: 400m),
            Dividend(700.00m),
            new Step(TaxEventKind.Acquisition, 400m, 5_000.00m, Booked: 200m),
            Split(1_600m),
            Sell(1_000m, 13_000.00m),
            Dividend(660.00m),
            Sell(500m, 6_000.00m),
            Buy(500m, 4_900.00m)
        ], instrumentId, openingBooked: 1_000m);

    /// <summary>Example 3 of §1.1: <c>RRR</c>, bought and partly sold at a loss.</summary>
    public static TaxHolding Example3Rrr(int instrumentId = Rrr) =>
        Holding(0m, 0.00m, 1_600.00m, [Buy(300m, 9_000.00m), Sell(100m, 900.00m)], instrumentId);
}
