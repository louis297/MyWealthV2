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
    public static FifIncome Compute(int taxYear, IReadOnlyList<TaxHolding> holdings, int decimalPlaces) =>
        new(0m, null, null, null, false, [], []);
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
