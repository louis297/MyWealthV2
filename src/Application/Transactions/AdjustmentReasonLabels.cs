using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.Transactions;

/// <summary>Adjustments §9: the label of each <see cref="AdjustmentReason"/>, shared by the portals and the report.</summary>
public static class AdjustmentReasonLabels
{
    public static string For(AdjustmentReason reason) => reason switch
    {
        AdjustmentReason.EntryError => "Entry error",
        AdjustmentReason.CorporateAction => "Corporate action",
        AdjustmentReason.ReconciliationBreak => "Reconciliation",
        AdjustmentReason.Other => "Other",
        _ => reason.ToString()
    };
}
