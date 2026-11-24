namespace MyWealthV2.Domain.Enums;

/// <summary>In-kind-transfers R54: the outcome of a custodian reconciliation run.</summary>
public enum CustodianReconciliationStatus
{
    Agreed = 0,
    Breaks = 1,
    NoPositions = 2
}
