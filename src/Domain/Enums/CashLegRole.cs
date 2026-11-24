namespace MyWealthV2.Domain.Enums;

/// <summary>What a cash leg is for (trade-fees R14, R18; interest-run R25). Later roles are added by their own specs.</summary>
public enum CashLegRole
{
    Principal = 0,
    Fee = 1,

    /// <summary>An amount withheld from the Transaction's income and owed to a tax authority, always negative, with
    /// one tax detail (interest-run R25).</summary>
    Tax = 2,

    /// <summary>The part of a dividend taken in new shares: always negative, only on a <c>Dividend</c>, at most one,
    /// and only together with its security leg (dividends R40).</summary>
    Reinvestment = 3
}
