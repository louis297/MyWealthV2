namespace MyWealthV2.Domain.Enums;

/// <summary>Why an entitlement was not applied (corporate-actions R25).</summary>
public enum EntitlementReason
{
    /// <summary>The account no longer holds the instrument.</summary>
    NoHolding = 0,

    /// <summary>The quantity after would be 0 or the same as before.</summary>
    TooSmall = 1
}
