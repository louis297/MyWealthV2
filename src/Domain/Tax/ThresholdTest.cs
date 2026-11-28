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
    /// <see cref="FifThreshold.For"/> of the year; <paramref name="decimalPlaces"/> are NZD's.
    /// </summary>
    public static ThresholdTest Compute(
        int taxYear,
        TaxCostWalk walk,
        IReadOnlyDictionary<int, TaxTreatment> treatments,
        IReadOnlyDictionary<(int InstrumentId, int TaxYear), FifTreatment>? earlierTreatments,
        decimal? threshold,
        int decimalPlaces)
    {
        var from = TaxYear.Start(taxYear);
        var to = TaxYear.End(taxYear);
        var zero = TaxFx.Zero(decimalPlaces);

        // R16: the tax holdings that count, by their treatment for the year.
        var counted = walk.Steps.Select(step => step.Event.InstrumentId)
            .Distinct()
            .Where(id => Counts(TreatmentOf(treatments, id).FifTreatment))
            .ToDictionary(id => id, id => walk.At(id, from).Cost);
        decimal Total() => counted.Values.Aggregate(zero, (sum, cost) => sum + (cost ?? 0m));

        // R17, R18: the total at the opening and after every event of the year on a tax holding that counts.
        var opening = Total();
        var peak = new ThresholdPeak(opening, from, null, null);
        var steps = new List<ThresholdStep>();
        var sequences = new Dictionary<int, int>();
        foreach (var step in walk.Steps.Where(step => step.Event.BookedAt >= from && step.Event.BookedAt < to))
        {
            var instrumentId = step.Event.InstrumentId;
            var sequence = sequences[instrumentId] = sequences.GetValueOrDefault(instrumentId) + 1;
            if (!counted.TryGetValue(instrumentId, out var before))
            {
                continue;
            }

            var previous = Total();
            counted[instrumentId] = step.CostAfter;
            var total = Total();
            if (total != previous || (before is not null && step.CostAfter is null))
            {
                steps.Add(new ThresholdStep(step.Event, sequence, step.Change, total));
            }

            if (total > peak.Total)
            {
                peak = new ThresholdPeak(total, step.Event.BookedAt, instrumentId, step.Event);
            }
        }

        var incomplete = Reasons(taxYear, walk, treatments, earlierTreatments, threshold, counted.Keys.ToHashSet());
        return new ThresholdTest(threshold, opening, peak, Total(), OutcomeOf(peak.Total, threshold, incomplete.Count == 0), incomplete, steps);
    }

    /// <summary>R20, R22: every reason of the table that applies, in the order of the table, then by Instrument.</summary>
    private static List<ThresholdIncomplete> Reasons(
        int taxYear,
        TaxCostWalk walk,
        IReadOnlyDictionary<int, TaxTreatment> treatments,
        IReadOnlyDictionary<(int InstrumentId, int TaxYear), FifTreatment>? earlierTreatments,
        decimal? threshold,
        IReadOnlySet<int> counted)
    {
        var from = TaxYear.Start(taxYear);
        var to = TaxYear.End(taxYear);
        var instruments = walk.Steps.Select(step => step.Event.InstrumentId).Distinct().Order().ToList();
        List<TaxCostStep> OfYear(int id) =>
            walk.Steps.Where(step => step.Event.InstrumentId == id && step.Event.BookedAt >= from && step.Event.BookedAt < to).ToList();

        var reasons = new List<ThresholdIncomplete>();

        // A tax holding of the year (tax-year-events R23) that is not classified may be an attributing interest.
        reasons.AddRange(instruments
            .Where(id => TreatmentOf(treatments, id).FifTreatment == FifTreatment.NotClassified
                         && (walk.At(id, from).Quantity != 0m || OfYear(id).Count > 0))
            .Select(id => new ThresholdIncomplete(ThresholdIncompleteReason.NotClassified, id)));

        // R13: a cost that counts and is unknown at the opening or after an event of the year.
        reasons.AddRange(instruments
            .Where(id => counted.Contains(id) && (walk.At(id, from).Cost is null || OfYear(id).Any(step => step.CostAfter is null)))
            .Select(id => new ThresholdIncomplete(ThresholdIncompleteReason.CostUnknown, id)));

        reasons.AddRange(instruments
            .Where(id => counted.Contains(id) && TreatmentChanged(id, taxYear, walk, earlierTreatments))
            .Select(id => new ThresholdIncomplete(ThresholdIncompleteReason.TreatmentChanged, id)));

        if (threshold is null)
        {
            reasons.Add(new ThresholdIncomplete(ThresholdIncompleteReason.NoThreshold, null));
        }

        return reasons;
    }

    /// <summary>
    /// R22: a tax holding held at the opening, whose Instrument is <c>NotForeign</c> or exempt for a tax year from the
    /// one in which its quantity last left 0 before the opening to the year before. A year not classified is skipped. A
    /// tax holding not held at the opening has no earlier year to check.
    /// </summary>
    private static bool TreatmentChanged(
        int instrumentId, int taxYear, TaxCostWalk walk, IReadOnlyDictionary<(int InstrumentId, int TaxYear), FifTreatment>? earlierTreatments)
    {
        var from = TaxYear.Start(taxYear);
        if (earlierTreatments is null || walk.At(instrumentId, from).Quantity == 0m)
        {
            return false;
        }

        DateTimeOffset? leftZero = null;
        var quantity = 0m;
        foreach (var step in walk.Steps.Where(step => step.Event.InstrumentId == instrumentId && step.Event.BookedAt < from))
        {
            if (quantity == 0m && step.QuantityAfter != 0m)
            {
                leftZero = step.Event.BookedAt;
            }

            quantity = step.QuantityAfter;
        }

        for (var year = TaxYear.Of(leftZero!.Value); year < taxYear; year++)
        {
            if (earlierTreatments.GetValueOrDefault((instrumentId, year)) is FifTreatment.NotForeign or FifTreatment.ExemptAsxListed or FifTreatment.ExemptOther)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>R16: an attributing interest counts.</summary>
    private static bool Counts(FifTreatment treatment) => treatment is FifTreatment.Attributing or FifTreatment.AttributingNoFdr;

    /// <summary>R19, R21: above when the highest total is more than the threshold, the known costs being a floor; not
    /// above only when complete; otherwise unknown.</summary>
    private static ThresholdOutcome OutcomeOf(decimal peak, decimal? threshold, bool complete) =>
        threshold is not { } amount ? ThresholdOutcome.Unknown
        : peak > amount ? ThresholdOutcome.Above
        : complete ? ThresholdOutcome.NotAbove
        : ThresholdOutcome.Unknown;

    private static TaxTreatment TreatmentOf(IReadOnlyDictionary<int, TaxTreatment> treatments, int instrumentId) =>
        treatments.GetValueOrDefault(instrumentId) ?? TaxTreatment.NotClassified();
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
