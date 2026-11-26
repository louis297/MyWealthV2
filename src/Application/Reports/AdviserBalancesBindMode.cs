namespace MyWealthV2.Application.Reports;

/// <summary>
/// What the Adviser Balances binder binds (valuation-history R16, R20): a manual request, checked against the last
/// final day of the moment of the request; a schedule, which takes no <c>date</c>; or a stored snapshot, which was
/// checked when it was made and is bound again as it is.
/// </summary>
public readonly record struct AdviserBalancesBindMode(AdviserBalancesBindKind Kind, DateOnly LastFinalDay)
{
    public static AdviserBalancesBindMode Request(DateOnly lastFinalDay) => new(AdviserBalancesBindKind.Request, lastFinalDay);

    public static AdviserBalancesBindMode Schedule => new(AdviserBalancesBindKind.Schedule, default);

    public static AdviserBalancesBindMode Snapshot => new(AdviserBalancesBindKind.Snapshot, default);
}

public enum AdviserBalancesBindKind
{
    Request = 0,
    Schedule = 1,
    Snapshot = 2
}
