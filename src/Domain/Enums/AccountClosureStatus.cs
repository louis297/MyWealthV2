namespace MyWealthV2.Domain.Enums;

/// <summary>Where an account closure is (account-closure §4.4). Only a closure with a payout is ever InProgress.</summary>
public enum AccountClosureStatus
{
    InProgress = 0,
    Completed = 1,
    Cancelled = 2
}
