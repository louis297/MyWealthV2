namespace MyWealthV2.Domain.Tax;

/// <summary>
/// The tax cost of one tax holding in one tax year (tax-cost-threshold R3, R13, R24): at the opening and at the closing,
/// each <c>null</c> while unknown, and the first point it was unknown, which may be in an earlier tax year.
/// </summary>
public sealed record TaxCost(TaxCostStatus Status, decimal? Opening, decimal? Closing, TaxCostUnknown? Unknown)
{
    /// <summary>R4: a treatment that needs no tax cost.</summary>
    public static TaxCost NotRequired { get; } = new(TaxCostStatus.NotRequired, null, null, null);
}

/// <summary>R24: the tax cost of one event: its change and the cost after it, each <c>null</c> while unknown, and the
/// rate row of a carried cost or a correction.</summary>
public sealed record TaxEventCost(decimal? Change, decimal? After, Valuations.StoredRate? Fx);

/// <summary>R24: whether a tax holding's cost is known through the year.</summary>
public enum TaxCostStatus
{
    Known = 0,
    Unknown = 1,

    /// <summary>R4: the treatment needs no tax cost; no rate is read.</summary>
    NotRequired = 2
}
