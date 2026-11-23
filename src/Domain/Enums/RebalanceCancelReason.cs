namespace MyWealthV2.Domain.Enums;

/// <summary>Why a rebalance was cancelled (docs/features/rebalances.md R3, R4, R19, R22).</summary>
public enum RebalanceCancelReason
{
    /// <summary>A person cancelled it.</summary>
    Requested = 0,

    /// <summary>A newer proposal for the account replaced it.</summary>
    Superseded = 1,

    /// <summary>Its assignment ended (unassign, disable, or another portfolio assigned).</summary>
    Unassigned = 2
}
