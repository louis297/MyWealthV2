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
    AmountPrecision = 6,

    /// <summary>The line names a withdrawal's payment but its amount differs, or the withdrawal is already paid
    /// (withdrawals R40, R42).</summary>
    PaymentMismatch = 7,

    /// <summary>A debit line the platform did not cause (bank-returns R2, R4).</summary>
    UnknownPayment = 8,

    /// <summary>A line naming a line it takes back that the platform cannot act on (bank-returns R3, R8).</summary>
    UnknownReturn = 9,

    /// <summary>A recall of a credited receipt whose cash is no longer available (bank-returns R3).</summary>
    RecallNotCovered = 10,

    /// <summary>The bank refused to send a receipt back (bank-returns R6).</summary>
    ReturnRejected = 11,

    /// <summary>The reference names an Account with an account closure in progress (account-closure R14).</summary>
    AccountClosing = 12,

    /// <summary>A receipt that names a trade settlement it does not match (trade-settlement R19).</summary>
    SettlementMismatch = 13
}
