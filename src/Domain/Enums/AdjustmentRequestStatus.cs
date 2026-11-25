namespace MyWealthV2.Domain.Enums;

/// <summary>The status of an adjustment request (adjustment-review R6). A request is open while it is
/// <c>Requested</c>; the three end statuses never change.</summary>
public enum AdjustmentRequestStatus
{
    Requested = 0,
    Approved = 1,
    Rejected = 2,
    Cancelled = 3
}
