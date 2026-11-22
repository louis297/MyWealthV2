namespace MyWealthV2.Domain.Interest;

/// <summary>Interest-run R26: the earliest and latest day an interest posting pays, and the number of accrual rows.</summary>
public sealed record InterestPeriod(DateOnly From, DateOnly To, int Days);
