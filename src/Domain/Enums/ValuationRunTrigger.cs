namespace MyWealthV2.Domain.Enums;

/// <summary>What started a valuation run (valuation-run R13).</summary>
public enum ValuationRunTrigger
{
    /// <summary>The daily <c>worker</c> job (R14).</summary>
    Scheduled = 0,

    /// <summary>A SystemAdmin through a date (R15).</summary>
    Manual = 1
}
