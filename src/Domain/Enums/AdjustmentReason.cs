namespace MyWealthV2.Domain.Enums;

/// <summary>Why an adjustment was entered (adjustments R7). The list changes only in a spec.</summary>
public enum AdjustmentReason
{
    EntryError = 0,
    CorporateAction = 1,
    ReconciliationBreak = 2,
    Other = 3
}
