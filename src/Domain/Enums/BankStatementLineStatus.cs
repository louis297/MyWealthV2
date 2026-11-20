namespace MyWealthV2.Domain.Enums;

/// <summary>Where a bank statement line is in matching and suspense (deposit-matching §5).</summary>
public enum BankStatementLineStatus
{
    Received = 0,
    Matched = 1,
    Unmatched = 2,
    Ignored = 3,
    ReturnRequested = 4
}
