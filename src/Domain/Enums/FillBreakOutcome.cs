namespace MyWealthV2.Domain.Enums;

/// <summary>What the platform did with a fill the broker broke (trade-settlement R28–R31).</summary>
public enum FillBreakOutcome
{
    /// <summary>The flow posted the Reversal of the fill's Transaction.</summary>
    Reversed = 0,

    /// <summary>The fill was never posted, so nothing was written.</summary>
    NotPosted = 1,

    /// <summary>The Transaction could not be reversed; the break waits for a TenantAdmin.</summary>
    NotApplied = 2,

    /// <summary>A TenantAdmin corrected the account and resolved the break.</summary>
    ResolvedByHand = 3
}
