namespace MyWealthV2.Domain.ValueObjects;

/// <summary>
/// The whole fee calculation for one amount (trade-fees R8, R32): the band slices, the unrounded banded amount,
/// the limit that applied, and the rounded fee.
/// </summary>
public sealed record FeeCalculation(
    decimal Amount,
    IReadOnlyList<FeeBandSlice> Bands,
    decimal Banded,
    FeeLimit? LimitApplied,
    decimal Fee);
