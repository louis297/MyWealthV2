namespace MyWealthV2.Domain.Enums;

/// <summary>Where a withdrawal is (withdrawals R24). Open while Requested, Approved or Sent.</summary>
public enum WithdrawalStatus
{
    Requested = 0,
    Approved = 1,
    Sent = 2,
    Paid = 3,
    Rejected = 4,
    Cancelled = 5,
    Failed = 6,
    Returned = 7
}
