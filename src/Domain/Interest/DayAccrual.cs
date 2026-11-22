namespace MyWealthV2.Domain.Interest;

/// <summary>Interest-run R17, R18: one day's interest (8 decimals) and the effective annual rate (4 decimals).</summary>
public sealed record DayAccrual(decimal Amount, decimal EffectiveRatePercent);
