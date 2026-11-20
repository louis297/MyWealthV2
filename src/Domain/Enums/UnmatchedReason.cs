namespace MyWealthV2.Domain.Enums;

/// <summary>Why the platform could not place a receipt, in the order the rules test them (deposit-matching R18).</summary>
public enum UnmatchedReason
{
    NoReference = 0,
    UnknownReference = 1,
    SeveralReferences = 2,
    CurrencyMismatch = 3,
    AccountClosed = 4,
    CustomerDisabled = 5,
    AmountPrecision = 6
}
