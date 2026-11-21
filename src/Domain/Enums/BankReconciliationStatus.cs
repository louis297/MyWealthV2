namespace MyWealthV2.Domain.Enums;

/// <summary>The outcome of a bank reconciliation run (bank-reconciliation R12).</summary>
public enum BankReconciliationStatus
{
    Agreed = 0,
    Breaks = 1,
    NoBankBalance = 2
}
