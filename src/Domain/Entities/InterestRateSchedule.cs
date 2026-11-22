using MyWealthV2.Domain.Interest;
using MyWealthV2.Domain.ValueObjects;

namespace MyWealthV2.Domain.Entities;

/// <summary>
/// A tenant's interest rate schedule for one cash currency from <see cref="EffectiveFrom"/> (interest-run R9–R14):
/// marginal bands of annual rates, reusing <see cref="FeeBands"/>. A row in effect (on or before the cut-off date,
/// R12) never changes.
/// </summary>
public class InterestRateSchedule : BaseAuditableEntity
{
    /// <summary>R17: Actual/365 Fixed for every currency and year.</summary>
    public const int DaysInYear = 365;

    /// <summary>R17: a daily accrual is kept to 8 decimals.</summary>
    public const int AccrualDecimalPlaces = 8;

    private readonly List<InterestRateBand> _bands = [];

    private InterestRateSchedule()
    {
    }

    public Guid PublicId { get; private set; }

    public int TenantId { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    public DateOnly EffectiveFrom { get; private set; }

    public byte[] RowVersion { get; private set; } = null!;

    public IReadOnlyList<InterestRateBand> BandRows => _bands;

    /// <summary>The bands in ascending order.</summary>
    public IReadOnlyList<FeeBand> Bands =>
        _bands.OrderBy(band => band.FromAmount).Select(band => new FeeBand(band.FromAmount, band.RatePercent)).ToList();

    public static InterestRateSchedule Create(
        int tenantId,
        Currency currency,
        DateOnly effectiveFrom,
        IReadOnlyList<FeeBand> bands,
        DateOnly cutOffDate,
        Guid? publicId = null) =>
        throw new NotImplementedException();

    /// <summary>R12: a row is in effect when it starts on or before the cut-off date.</summary>
    public bool IsInEffect(DateOnly cutOffDate) => throw new NotImplementedException();

    /// <summary>R13: replaces the date and the whole band set of a pending row. <c>Currency</c> is immutable.</summary>
    public void Update(DateOnly effectiveFrom, IReadOnlyList<FeeBand> bands, int decimalPlaces, DateOnly cutOffDate) =>
        throw new NotImplementedException();

    /// <summary>R13: refuses delete of a row in effect.</summary>
    public void EnsureDeletable(DateOnly cutOffDate) => throw new NotImplementedException();

    /// <summary>R17, R18: the interest one day balance earns, and the effective annual rate.</summary>
    public DayAccrual AccrualFor(decimal dayBalance) => throw new NotImplementedException();
}
