namespace MyWealthV2.Domain.ValueObjects;

/// <summary>
/// Ordered marginal fee bands (trade-fees R5). The first band starts at 0, starts are strictly ascending and the last
/// band has no upper end. Shared by trade fees and, later, advisory fees.
/// </summary>
public sealed class FeeBands : ValueObject
{
    public const int MaxBands = 10;

    public const int RateDecimalPlaces = 4;

    private readonly List<FeeBand> _items;

    private FeeBands(List<FeeBand> items)
    {
        _items = items;
    }

    public IReadOnlyList<FeeBand> Items => _items;

    /// <summary>Checks the R5 band shape. <paramref name="decimalPlaces"/> limits the decimals of each <c>from</c>.</summary>
    public static FeeBands Create(IReadOnlyList<FeeBand> bands, int decimalPlaces) =>
        throw new NotImplementedException();

    /// <summary>The slice of <paramref name="amount"/> inside each band whose start is below it, with the band's fee.</summary>
    public IReadOnlyList<FeeBandSlice> Slices(decimal amount) => throw new NotImplementedException();

    /// <summary>The banded amount: the sum of the band fees, unrounded.</summary>
    public decimal Apply(decimal amount) => throw new NotImplementedException();

    protected override IEnumerable<object> GetEqualityComponents()
    {
        foreach (var band in _items)
        {
            yield return band.From;
            yield return band.RatePercent;
        }
    }
}
