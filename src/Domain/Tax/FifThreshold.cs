namespace MyWealthV2.Domain.Tax;

/// <summary>
/// Tax-cost-threshold R15: the FIF threshold of a tax year in NZD, a constant that changes only with an amendment to
/// the spec. NZ$50,000 for tax years 2009 to 2026. From 2027 there is no value yet: the Bill that raises it was not law
/// on 2026-11-28, and tax year 2027 can first be read on 2 April 2027.
/// </summary>
public static class FifThreshold
{
    private static readonly (int From, int To, decimal Amount)[] Rows = [(2009, 2026, 50_000.00m)];

    /// <summary>The threshold of <paramref name="taxYear"/>, or none.</summary>
    public static decimal? For(int taxYear) =>
        Rows.Where(row => taxYear >= row.From && taxYear <= row.To).Select(row => (decimal?)row.Amount).FirstOrDefault();
}
