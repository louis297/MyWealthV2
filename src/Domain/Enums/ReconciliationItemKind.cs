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
    OtherClientMoneyAccount = 11,

    /// <summary>Interest the bank paid on the trust account, set aside by the platform (interest-run R41).</summary>
    BankInterest = 12,

    /// <summary>Interest the interest run credited to Customers (interest-run R41).</summary>
    InterestCredited = 13,

    /// <summary>Tax withheld from that interest and still in the trust account (interest-run R41).</summary>
    TaxWithheld = 14,

    /// <summary>The cash of order fills whose trade settlement has not completed (trade-settlement R23), Timing.</summary>
    UnsettledTrades = 15,

    /// <summary>The cash of order fills whose settlement failed or is disputed, or that were broken and not reversed
    /// (trade-settlement R23), Break.</summary>
    TradesNeedingAction = 16,

    /// <summary>Fills still <c>Pending</c> in a completed trade settlement (trade-settlement R24), Break.</summary>
    UnbookedFills = 17,

    /// <summary>The trade fees of order fills, held in the trust account (trade-settlement R25), Known.</summary>
    TradeFees = 18,

    /// <summary>A receipt matched to a migration batch that is not posted yet: money in the trust account that is not
    /// in the ledger yet (migration-batches R32), Timing.</summary>
    MigrationNotPosted = 19,

    /// <summary>Dividends received on the account with an entitlement still <c>Pending</c>: money in the trust account
    /// not on the Customers' accounts yet (dividends R44), Timing.</summary>
    DividendsNotPosted = 20,

    /// <summary>The rest of the other dividends received on the account: the custodian's rounding, a difference a
    /// person accepted, and dividends credited in another currency (dividends R44, R45), Known.</summary>
    DividendDifference = 21
}
