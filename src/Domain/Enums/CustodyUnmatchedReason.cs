namespace MyWealthV2.Domain.Enums;

/// <summary>In-kind-transfers R15–R18, R36, R39: why a custody movement waits in suspense.</summary>
public enum CustodyUnmatchedReason
{
    NoInstruction = 0,
    UnknownInstruction = 1,
    TransferNotOpen = 2,
    TransferMismatch = 3,
    AccountClosed = 4,
    InsufficientHolding = 5,
    UnknownTrade = 6,
    TradeMismatch = 7,
    UnknownCorporateAction = 8,
    UnsupportedKind = 9
}
