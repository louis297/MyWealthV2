namespace MyWealthV2.Domain.Enums;

/// <summary>Why the custodian rejected a reinvestment election (dividends R22, R58).</summary>
public enum ElectionRejectReason
{
    /// <summary>The custodian knows no dividend with that id and symbol.</summary>
    UnknownCorporateAction = 0,

    /// <summary>The dividend has no reinvestment plan.</summary>
    NoPlan = 1,

    /// <summary>The dividend has not gone ex, the account is not entitled, or the quantity is above its position.</summary>
    NotEntitled = 2,

    /// <summary>The custodian has already paid the dividend.</summary>
    TooLate = 3,

    /// <summary>A reason the adapter does not know.</summary>
    Other = 4
}
