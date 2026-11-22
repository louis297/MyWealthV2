namespace MyWealthV2.Domain.Enums;

/// <summary>What started an interest run (interest-run R30).</summary>
public enum InterestRunTrigger
{
    /// <summary>The daily <c>worker</c> job (R31).</summary>
    Scheduled = 0,

    /// <summary>A SystemAdmin for an as-of date (R32).</summary>
    Manual = 1,

    /// <summary>The final interest of one account when its closure is requested (R33).</summary>
    AccountClosure = 2
}
