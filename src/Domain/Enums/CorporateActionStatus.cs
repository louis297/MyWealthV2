namespace MyWealthV2.Domain.Enums;

/// <summary>The status of a corporate action (corporate-actions R6). No status ever goes back.</summary>
public enum CorporateActionStatus
{
    /// <summary>Known; changes nothing yet and may still be changed or cancelled by its origin.</summary>
    Announced = 0,

    /// <summary>Took effect at <c>EffectiveAt</c>; its entitlements exist.</summary>
    Effective = 1,

    /// <summary>No entitlement is <c>Pending</c> any more.</summary>
    Applied = 2,

    /// <summary>Withdrawn before it took effect.</summary>
    Cancelled = 3
}
