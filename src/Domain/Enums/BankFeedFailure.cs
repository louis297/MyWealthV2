namespace MyWealthV2.Domain.Enums;

/// <summary>Why the last read of a client money account's statement failed (deposit-matching R4, R7, R10, R49).</summary>
public enum BankFeedFailure
{
    Unreachable = 0,
    Timeout = 1,
    Unauthorized = 2,
    Unavailable = 3,
    UnknownAccount = 4,
    BadResponse = 5,
    InvalidLine = 6
}
