namespace MyWealthV2.Domain.Tax;

/// <summary>
/// Tax-cost-threshold R15: the FIF threshold of a tax year in NZD, a constant that changes only with an amendment to
/// the spec.
/// </summary>
public static class FifThreshold
{
    public static decimal? For(int taxYear) => 0m;
}
