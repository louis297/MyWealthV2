namespace MyWealthV2.Domain.Enums;

/// <summary>
/// What a reconciliation item explains (bank-reconciliation R9–R11). The order of the values is the order of the
/// items within a class (R13).
/// </summary>
public enum ReconciliationItemKind
{
    Unexplained = 0,
    StatementDifference = 1,
    Suspense = 2,
    NotProcessed = 3,
    ReturnInProgress = 4,
    OpeningBalance = 5,
    Ignored = 6,
    Trades = 7,
    FxConversions = 8,
    Adjustments = 9,
    OtherLedgerCash = 10,
    OtherClientMoneyAccount = 11
}
