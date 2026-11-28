namespace MyWealthV2.Domain.Tax;

/// <summary>
/// FIF income of one Customer for one tax year (docs/features/fif-income.md): the income of each tax holding under the
/// fair dividend rate method, with its quick sale, and under the comparative value method, and the totals of both. A
/// value worked out by <see cref="Compute"/> from the tax holdings of the year; it reads no clock and no store, and
/// nothing is stored.
/// </summary>
public sealed record FifIncome(
    decimal Rate,
    FifFdrTotals? Fdr,
    FifCvTotals? Cv,
    FifLowerMethod? Lower,
    bool Complete,
    IReadOnlyList<FifIncomeIncomplete> Incomplete,
    IReadOnlyList<TaxHoldingIncome> Holdings)
{
    /// <summary>R3–R25: the income of each tax holding of <paramref name="holdings"/>, in their order, and the totals.
    /// <paramref name="decimalPlaces"/> is NZD's.</summary>
    public static FifIncome Compute(int taxYear, IReadOnlyList<TaxHolding> holdings, int decimalPlaces)
    {
        var rate = FairDividendRate.For(taxYear);
        var incomes = holdings.Select(holding => Income(holding, rate, decimalPlaces)).ToList();
        return new FifIncome(rate, null, null, null, false, [], incomes);
    }

    /// <summary>R3, R4: the status of a tax holding; R8: its FDR amount when it is computed.</summary>
    private static TaxHoldingIncome Income(TaxHolding holding, decimal rate, int decimalPlaces)
    {
        TaxHoldingIncome NotComputed(FifIncomeReason reason) => new(holding.InstrumentId, FifIncomeStatus.NotComputed, reason, null, null, null);

        switch (holding.Treatment.FifTreatment)
        {
            case FifTreatment.NotClassified:
                return NotComputed(FifIncomeReason.NotClassified);
            case FifTreatment.AttributingNoFdr:
                return NotComputed(FifIncomeReason.FdrNotAllowed);
            case FifTreatment.Attributing when !holding.Complete:
                return NotComputed(FifIncomeReason.HoldingIncomplete);
            case FifTreatment.Attributing:
                break;
            default:
                return new TaxHoldingIncome(holding.InstrumentId, FifIncomeStatus.NotRequired, null, null, null, null);
        }

        var openingValue = holding.Opening.Value!.Value;
        var fdr = new FifFdr(openingValue, Round(rate * openingValue, decimalPlaces));
        return new TaxHoldingIncome(holding.InstrumentId, FifIncomeStatus.Computed, null, fdr, QuickSaleOf(holding, rate, decimalPlaces), null);
    }

    /// <summary>R9–R12: the quick sale of a computed tax holding.</summary>
    private static QuickSale QuickSaleOf(TaxHolding holding, decimal rate, int decimalPlaces)
    {
        var opening = holding.Opening.ComparableQuantity!.Value;
        var closing = holding.Closing.ComparableQuantity!.Value;
        var peak = holding.PeakQuantity!.Value;
        var purchases = Shares(holding).Where(e => IsPurchase(e.Event.Event.Kind)).ToList();

        // R10: the lesser of the rise above the opening and the fall from the peak.
        var differential = Math.Max(0m, Math.Min(peak - opening, peak - closing));

        // R11: one average over every purchase of the year; none without a purchase.
        var purchased = purchases.Sum(e => e.Shares);
        var purchaseCost = purchases.Sum(e => e.Event.Amount!.Value);
        var hasAverage = purchased > 0m;

        // R12: multiplied first and divided after.
        var peakHoldingAmount = hasAverage ? Math.Max(0m, Round(rate * differential * purchaseCost / purchased, decimalPlaces)) : Zero(decimalPlaces);
        return new QuickSale(
            opening,
            peak,
            closing,
            differential,
            purchased,
            purchaseCost,
            hasAverage ? decimal.Round(purchaseCost / purchased, 8, MidpointRounding.AwayFromZero) : null,
            peakHoldingAmount,
            0m,
            Zero(decimalPlaces),
            Zero(decimalPlaces),
            Zero(decimalPlaces),
            Zero(decimalPlaces),
            [],
            [],
            []);
    }

    /// <summary>R9: the shares of each event, the change of the comparable quantity at it, in the order of the year.</summary>
    private static List<(TaxYearEvent Event, decimal Shares)> Shares(TaxHolding holding)
    {
        var before = holding.Opening.ComparableQuantity!.Value;
        var shares = new List<(TaxYearEvent, decimal)>();
        foreach (var yearEvent in holding.Events.OrderBy(e => e.Sequence))
        {
            var after = yearEvent.ComparableQuantityAfter!.Value;
            shares.Add((yearEvent, after - before));
            before = after;
        }

        return shares;
    }

    /// <summary>R6: an acquisition and an arrival are purchases.</summary>
    private static bool IsPurchase(TaxEventKind kind) => kind is TaxEventKind.Acquisition or TaxEventKind.Arrival;

    /// <summary>R6: a disposal and a departure are sales.</summary>
    private static bool IsSale(TaxEventKind kind) => kind is TaxEventKind.Disposal or TaxEventKind.Departure;

    /// <summary>0 with NZD's places, as every other NZD figure is written.</summary>
    private static decimal Zero(int decimalPlaces) => decimal.Round(0.000000m, decimalPlaces);

    /// <summary>R24: every figure of a tax holding is rounded to NZD's places, away from zero.</summary>
    private static decimal Round(decimal amount, int decimalPlaces) => decimal.Round(amount, decimalPlaces, MidpointRounding.AwayFromZero);
}

/// <summary>R20, R26: the totals of the fair dividend rate method over the computed tax holdings.</summary>
public sealed record FifFdrTotals(
    decimal OpeningValue,
    decimal Amount,
    decimal PeakHoldingAmount,
    decimal QuickSaleGainAmount,
    decimal QuickSaleAdjustment,
    decimal Income);

/// <summary>R21, R26: the sum of the CV amounts and the CV income.</summary>
public sealed record FifCvTotals(decimal Amount, decimal Income);

/// <summary>R23: a tax holding that is not computed, and why.</summary>
public sealed record FifIncomeIncomplete(FifIncomeReason Reason, int InstrumentId);

/// <summary>R3, R4, R26: the FIF income of one tax holding. <see cref="Reason"/> is set only when it is
/// <c>NotComputed</c>; the figures only when it is <c>Computed</c>.</summary>
public sealed record TaxHoldingIncome(
    int InstrumentId,
    FifIncomeStatus Status,
    FifIncomeReason? Reason,
    FifFdr? Fdr,
    QuickSale? QuickSale,
    FifCv? Cv);

/// <summary>R8: the opening value of a tax holding and its FDR amount.</summary>
public sealed record FifFdr(decimal OpeningValue, decimal Amount);

/// <summary>R9–R14: the quick sale of one tax holding. Every quantity is a comparable quantity.</summary>
public sealed record QuickSale(
    decimal OpeningQuantity,
    decimal PeakQuantity,
    decimal ClosingQuantity,
    decimal PeakHoldingDifferential,
    decimal PurchasedQuantity,
    decimal PurchaseCost,
    decimal? AverageCost,
    decimal PeakHoldingAmount,
    decimal MatchedQuantity,
    decimal Proceeds,
    decimal Dividends,
    decimal Cost,
    decimal GainAmount,
    IReadOnlyList<QuickSaleMatch> Matches,
    IReadOnlyList<QuickSaleSale> Sales,
    IReadOnlyList<QuickSaleDividendShare> DividendShares);

/// <summary>R13: shares taken from one purchase of the year by one sale.</summary>
public sealed record QuickSaleMatch(int PurchaseSequence, int SaleSequence, decimal Quantity);

/// <summary>R14: a sale of the year, the shares of it that were matched and their part of its proceeds.</summary>
public sealed record QuickSaleSale(int Sequence, decimal Quantity, decimal MatchedQuantity, decimal Amount, decimal Proceeds);

/// <summary>R14: the share of a dividend that belongs to the matched shares held when it was booked.</summary>
public sealed record QuickSaleDividendShare(int Sequence, decimal Amount, decimal HeldQuantity, decimal MatchedQuantity, decimal Share);

/// <summary>R16: the figures of the CV amount of one tax holding.</summary>
public sealed record FifCv(decimal OpeningValue, decimal ClosingValue, decimal Sales, decimal Dividends, decimal Purchases, decimal Amount);

/// <summary>R3, R26: whether the income of a tax holding is computed.</summary>
public enum FifIncomeStatus
{
    Computed = 0,
    NotComputed = 1,
    NotRequired = 2
}

/// <summary>R23: why the income of a tax holding is not computed.</summary>
public enum FifIncomeReason
{
    NotClassified = 0,
    HoldingIncomplete = 1,
    FdrNotAllowed = 2
}

/// <summary>R22: which total is lower. A comparison, not a choice.</summary>
public enum FifLowerMethod
{
    Fdr = 0,
    Cv = 1,
    Equal = 2
}
