namespace MyWealthV2.Domain.Enums;

/// <summary>What started a bank reconciliation run: the daily job or a TenantAdmin (bank-reconciliation R16, R17).</summary>
public enum ReconciliationTrigger
{
    Scheduled = 0,
    Manual = 1
}
