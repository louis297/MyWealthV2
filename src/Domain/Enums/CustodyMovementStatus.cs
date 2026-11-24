namespace MyWealthV2.Domain.Enums;

/// <summary>In-kind-transfers R13, R14, R18: where a custody movement is in its processing.</summary>
public enum CustodyMovementStatus
{
    Received = 0,
    Matched = 1,
    Unmatched = 2,
    Ignored = 3
}
