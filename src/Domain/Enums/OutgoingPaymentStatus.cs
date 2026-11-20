namespace MyWealthV2.Domain.Enums;

/// <summary>Where an outgoing payment is (withdrawals R31).</summary>
public enum OutgoingPaymentStatus
{
    Pending = 0,
    Accepted = 1,
    Rejected = 2,
    Cancelled = 3,
    Paid = 4,
    Returned = 5
}
