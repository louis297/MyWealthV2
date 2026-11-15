namespace MyWealthV2.Domain.ValueObjects;

/// <summary>The part of an amount inside one band and the band's fee on it, unrounded (trade-fees R32).</summary>
public sealed record FeeBandSlice(decimal From, decimal RatePercent, decimal Slice, decimal Fee);
