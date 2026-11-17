namespace MyWealthV2.Domain.Enums;

/// <summary>
/// Who holds the assets of an Account (ADR 0020). Set when the account is opened and never changed.
/// </summary>
public enum CustodyMode
{
    /// <summary>The assets are held outside the platform; the platform records what happened elsewhere.</summary>
    External = 0,

    /// <summary>The assets are held by the platform, which executes the trades. Always a Brokerage account.</summary>
    Custodial = 1
}
