namespace MyWealthV2.Domain.Enums;

/// <summary>Where a bank statement line is in matching and suspense (deposit-matching §5).</summary>
public enum BankStatementLineStatus
{
    Received = 0,
    Matched = 1,
    Unmatched = 2,
    Ignored = 3,
    ReturnRequested = 4,

    /// <summary>The bank accepted the return payment (bank-returns R6).</summary>
    ReturnSent = 5,

    /// <summary>The money went back to the payer, by a return or a recall (bank-returns R3, R7). Final.</summary>
    Returned = 6
}
