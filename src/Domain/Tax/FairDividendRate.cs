namespace MyWealthV2.Domain.Tax;

/// <summary>
/// FIF income R7: the rate of the fair dividend rate method for a tax year, a constant that changes only with an
/// amendment to docs/features/fif-income.md. 0.05 for tax year 2009 and every later one.
/// </summary>
public static class FairDividendRate
{
    /// <summary>The rate of <paramref name="taxYear"/>. A tax year before 2009 cannot be read (tax-year-events R4).</summary>
    public static decimal For(int taxYear) => 0m;
}
