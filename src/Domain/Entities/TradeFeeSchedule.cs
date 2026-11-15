using MyWealthV2.Domain.ValueObjects;

namespace MyWealthV2.Domain.Entities;

/// <summary>
/// A tenant's brokerage schedule for one quote currency from <see cref="EffectiveFrom"/> (trade-fees R5–R13).
/// A row in effect (on or before the cut-off date, R9) never changes.
/// </summary>
public class TradeFeeSchedule : BaseAuditableEntity
{
    private readonly List<TradeFeeBand> _bands = [];

    private TradeFeeSchedule()
    {
    }

    public Guid PublicId { get; private set; }

    public int TenantId { get; private set; }

    public string QuoteCurrency { get; private set; } = string.Empty;

    public DateOnly EffectiveFrom { get; private set; }

    public decimal Minimum { get; private set; }

    public decimal? Cap { get; private set; }

    public byte[] RowVersion { get; private set; } = null!;

    public IReadOnlyList<TradeFeeBand> BandRows => _bands;

    /// <summary>The bands in ascending order.</summary>
    public IReadOnlyList<FeeBand> Bands =>
        _bands.OrderBy(band => band.FromAmount).Select(band => new FeeBand(band.FromAmount, band.RatePercent)).ToList();

    public static TradeFeeSchedule Create(
        int tenantId,
        Currency quoteCurrency,
        DateOnly effectiveFrom,
        decimal minimum,
        decimal? cap,
        IReadOnlyList<FeeBand> bands,
        DateOnly cutOffDate,
        Guid? publicId = null)
    {
        if (quoteCurrency is null || string.IsNullOrWhiteSpace(quoteCurrency.Code))
        {
            throw new DomainException("Currency is required.");
        }

        var schedule = new TradeFeeSchedule
        {
            TenantId = tenantId,
            QuoteCurrency = quoteCurrency.Code.Trim().ToUpperInvariant(),
            PublicId = publicId ?? Guid.NewGuid()
        };
        schedule.Apply(effectiveFrom, minimum, cap, bands, quoteCurrency.DecimalPlaces, cutOffDate);
        return schedule;
    }

    /// <summary>A row is in effect when it starts on or before the cut-off date (R9).</summary>
    public bool IsInEffect(DateOnly cutOffDate) => EffectiveFrom <= cutOffDate;

    /// <summary>Replaces the dates, limits and bands of a pending row (R11). <c>QuoteCurrency</c> is immutable.</summary>
    public void Update(
        DateOnly effectiveFrom,
        decimal minimum,
        decimal? cap,
        IReadOnlyList<FeeBand> bands,
        int decimalPlaces,
        DateOnly cutOffDate)
    {
        EnsureDeletable(cutOffDate);
        Apply(effectiveFrom, minimum, cap, bands, decimalPlaces, cutOffDate);
    }

    /// <summary>Refuses delete of a row in effect (R11).</summary>
    public void EnsureDeletable(DateOnly cutOffDate)
    {
        if (IsInEffect(cutOffDate))
        {
            throw new TradeFeeScheduleInEffectException();
        }
    }

    /// <summary>The fee for a trade's gross amount in the quote currency (R8).</summary>
    public FeeCalculation FeeFor(decimal gross, int decimalPlaces) =>
        Calculate(gross, Minimum, Cap, FeeBands.Create(Bands, decimalPlaces), decimalPlaces);

    /// <summary>
    /// The R8 calculation for schedule values that need not be saved. <see cref="FeeFor"/> and the fee preview
    /// (R32) both use it, so there is one implementation.
    /// </summary>
    public static FeeCalculation Calculate(
        decimal gross,
        decimal minimum,
        decimal? cap,
        FeeBands bands,
        int decimalPlaces)
    {
        var slices = bands.Slices(gross);
        var banded = slices.Sum(slice => slice.Fee);
        FeeLimit? limit = null;
        var limited = banded;
        if (banded < minimum)
        {
            limited = minimum;
            limit = FeeLimit.Minimum;
        }

        if (cap is { } maximum && limited > maximum)
        {
            limited = maximum;
            limit = FeeLimit.Cap;
        }

        var fee = decimal.Round(limited, decimalPlaces, MidpointRounding.AwayFromZero);
        return new FeeCalculation(gross, slices, banded, limit, fee);
    }

    /// <summary>Checks the R5 limits: minimum <c>≥ 0</c>; cap null or <c>&gt; 0</c> and <c>≥</c> minimum; decimals.</summary>
    public static void EnsureLimits(decimal minimum, decimal? cap, int decimalPlaces)
    {
        if (minimum < 0m || !FeeBands.HasAtMostDecimals(minimum, decimalPlaces))
        {
            throw new DomainException($"The minimum must be at least 0 with at most {decimalPlaces} decimals.");
        }

        if (cap is { } maximum && (maximum <= 0m || maximum < minimum || !FeeBands.HasAtMostDecimals(maximum, decimalPlaces)))
        {
            throw new DomainException($"The cap must be above 0, at least the minimum, with at most {decimalPlaces} decimals.");
        }
    }

    private void Apply(
        DateOnly effectiveFrom,
        decimal minimum,
        decimal? cap,
        IReadOnlyList<FeeBand> bands,
        int decimalPlaces,
        DateOnly cutOffDate)
    {
        if (effectiveFrom <= cutOffDate)
        {
            throw new DomainException("EffectiveFrom must be after the cut-off date.");
        }

        EnsureLimits(minimum, cap, decimalPlaces);
        var shape = FeeBands.Create(bands, decimalPlaces);

        EffectiveFrom = effectiveFrom;
        Minimum = minimum;
        Cap = cap;
        _bands.Clear();
        _bands.AddRange(shape.Items.Select(band => TradeFeeBand.Create(TenantId, band.From, band.RatePercent)));
    }
}
