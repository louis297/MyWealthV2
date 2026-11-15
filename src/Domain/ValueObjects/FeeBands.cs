using MyWealthV2.Domain.Exceptions;

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
    public static FeeBands Create(IReadOnlyList<FeeBand> bands, int decimalPlaces)
    {
        if (bands is null || bands.Count is < 1 or > MaxBands)
        {
            throw new DomainException($"A schedule needs 1 to {MaxBands} bands.");
        }

        if (bands[0].From != 0m)
        {
            throw new DomainException("The first band must start at 0.");
        }

        for (var index = 0; index < bands.Count; index++)
        {
            var band = bands[index];
            if (index > 0 && band.From <= bands[index - 1].From)
            {
                throw new DomainException("Each band must start above the one before it.");
            }

            if (!HasAtMostDecimals(band.From, decimalPlaces))
            {
                throw new DomainException($"A band start can have at most {decimalPlaces} decimals.");
            }

            if (band.RatePercent is < 0m or > 100m || !HasAtMostDecimals(band.RatePercent, RateDecimalPlaces))
            {
                throw new DomainException($"A band rate must be between 0 and 100 with at most {RateDecimalPlaces} decimals.");
            }
        }

        return new FeeBands(bands.Select(band => new FeeBand(band.From, band.RatePercent)).ToList());
    }

    /// <summary>True when <paramref name="value"/> has no non-zero digit after <paramref name="decimalPlaces"/>.</summary>
    public static bool HasAtMostDecimals(decimal value, int decimalPlaces) =>
        decimal.Round(value, decimalPlaces, MidpointRounding.AwayFromZero) == value;

    /// <summary>The slice of <paramref name="amount"/> inside each band whose start is below it, with the band's fee.</summary>
    public IReadOnlyList<FeeBandSlice> Slices(decimal amount)
    {
        var slices = new List<FeeBandSlice>(_items.Count);
        for (var index = 0; index < _items.Count && _items[index].From < amount; index++)
        {
            var band = _items[index];
            var end = index + 1 < _items.Count ? Math.Min(amount, _items[index + 1].From) : amount;
            var slice = end - band.From;
            slices.Add(new FeeBandSlice(band.From, band.RatePercent, slice, band.RatePercent / 100m * slice));
        }

        return slices;
    }

    /// <summary>The banded amount: the sum of the band fees, unrounded.</summary>
    public decimal Apply(decimal amount) => Slices(amount).Sum(slice => slice.Fee);

    protected override IEnumerable<object> GetEqualityComponents()
    {
        foreach (var band in _items)
        {
            yield return band.From;
            yield return band.RatePercent;
        }
    }
}
