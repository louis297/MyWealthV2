namespace MyWealthV2.Domain.Enums;

/// <summary>Where a corporate action came from (corporate-actions R5).</summary>
public enum CorporateActionOrigin
{
    /// <summary>Entered by a TenantAdmin.</summary>
    Manual = 0,

    /// <summary>Created from a corporate action announcement; no person can change it.</summary>
    Feed = 1
}
