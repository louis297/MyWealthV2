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
        Guid? publicId = null) =>
        throw new NotImplementedException();

    /// <summary>A row is in effect when it starts on or before the cut-off date (R9).</summary>
    public bool IsInEffect(DateOnly cutOffDate) => throw new NotImplementedException();

    /// <summary>Replaces the dates, limits and bands of a pending row (R11). <c>QuoteCurrency</c> is immutable.</summary>
    public void Update(
        DateOnly effectiveFrom,
        decimal minimum,
        decimal? cap,
        IReadOnlyList<FeeBand> bands,
        int decimalPlaces,
        DateOnly cutOffDate) =>
        throw new NotImplementedException();

    /// <summary>Refuses delete of a row in effect (R11).</summary>
    public void EnsureDeletable(DateOnly cutOffDate) => throw new NotImplementedException();

    /// <summary>The fee for a trade's gross amount in the quote currency (R8).</summary>
    public FeeCalculation FeeFor(decimal gross, int decimalPlaces) => throw new NotImplementedException();

    /// <summary>
    /// The R8 calculation for schedule values that need not be saved. <see cref="FeeFor"/> and the fee preview
    /// (R32) both use it, so there is one implementation.
    /// </summary>
    public static FeeCalculation Calculate(
        decimal gross,
        decimal minimum,
        decimal? cap,
        FeeBands bands,
        int decimalPlaces) =>
        throw new NotImplementedException();

    /// <summary>Checks the R5 limits: minimum <c>≥ 0</c>; cap null or <c>&gt; 0</c> and <c>≥</c> minimum; decimals.</summary>
    public static void EnsureLimits(decimal minimum, decimal? cap, int decimalPlaces) =>
        throw new NotImplementedException();
}
