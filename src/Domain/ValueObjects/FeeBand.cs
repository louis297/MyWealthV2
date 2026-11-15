namespace MyWealthV2.Domain.ValueObjects;

/// <summary>One marginal band: <see cref="RatePercent"/> applies from <see cref="From"/> up to the next band's start.</summary>
public sealed record FeeBand(decimal From, decimal RatePercent);
