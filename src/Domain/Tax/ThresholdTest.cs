namespace MyWealthV2.Domain.Tax;

/// <summary>
/// The threshold test of one Customer and one tax year (tax-cost-threshold R15–R22): the total tax cost of the tax
/// holdings that count, at the opening, after every event of the year and at the closing; the highest total; the
/// outcome; and why it is incomplete. A value worked out by <see cref="Compute"/>, which reads no clock and no store.
/// </summary>
public sealed record ThresholdTest(
    decimal? Amount,
    decimal Opening,
    ThresholdPeak Peak,
    decimal Closing,
    ThresholdOutcome Outcome,
    IReadOnlyList<ThresholdIncomplete> Incomplete,
    IReadOnlyList<ThresholdStep> Steps)
{
    /// <summary>R20: complete if and only if no reason is listed.</summary>
    public bool Complete => Incomplete.Count == 0;

    /// <summary>
    /// R15–R22 over <paramref name="walk"/>. <paramref name="treatments"/> holds the treatment of each Instrument for
    /// <paramref name="taxYear"/> (none: not classified); <paramref name="earlierTreatments"/> the treatment of an
    /// Instrument for an earlier tax year (none: not classified); <paramref name="threshold"/> is
    /// <see cref="FifThreshold.For"/> of the year.
    /// </summary>
    public static ThresholdTest Compute(
        int taxYear,
        TaxCostWalk walk,
        IReadOnlyDictionary<int, TaxTreatment> treatments,
        IReadOnlyDictionary<(int InstrumentId, int TaxYear), FifTreatment>? earlierTreatments,
        decimal? threshold) =>
        new(null, -1m, new ThresholdPeak(-1m, default, null, null), -1m, ThresholdOutcome.Unknown, [], []);
}

/// <summary>R18: the highest total and the first point it was reached: the opening (no Instrument and no event), or an
/// event of the year.</summary>
public sealed record ThresholdPeak(decimal Total, DateTimeOffset At, int? InstrumentId, TaxEvent? Event);

/// <summary>R20: a reason the test is incomplete, with the tax holding it names (none for <c>NoThreshold</c>).</summary>
public sealed record ThresholdIncomplete(ThresholdIncompleteReason Reason, int? InstrumentId);

/// <summary>R24: an event of the year that changed the total or made a cost unknown, with its sequence in its tax
/// holding's events of the year, the change of that holding's cost (<c>null</c> when unknown) and the total after it.</summary>
public sealed record ThresholdStep(TaxEvent Event, int EventSequence, decimal? Change, decimal Total);

/// <summary>R19: the outcome of the test.</summary>
public enum ThresholdOutcome
{
    NotAbove = 0,
    Above = 1,
    Unknown = 2
}

/// <summary>R20: why a test is incomplete.</summary>
public enum ThresholdIncompleteReason
{
    NotClassified = 0,
    CostUnknown = 1,
    TreatmentChanged = 2,
    NoThreshold = 3
}
