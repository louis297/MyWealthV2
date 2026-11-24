namespace MyWealthV2.Domain.Enums;

/// <summary>In-kind-transfers R27: a transfer is open while it is <c>Requested</c>, <c>Approved</c> or <c>Sent</c>.</summary>
public enum InKindTransferStatus
{
    Requested = 0,
    Approved = 1,
    Sent = 2,
    Completed = 3,
    Rejected = 4,
    Cancelled = 5,
    Failed = 6,
    SetAside = 7
}
