namespace MyWealthV2.Domain.Enums;

/// <summary>The status of a corporate action entitlement (corporate-actions R22, R25).</summary>
public enum EntitlementStatus
{
    Pending = 0,
    Posted = 1,
    NotApplied = 2
}
