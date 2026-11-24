namespace MyWealthV2.Domain.Enums;

/// <summary>In-kind-transfers R28, R30–R32: the decision recorded on a transfer.</summary>
public enum InKindTransferDecision
{
    Approve = 0,
    Reject = 1,
    Cancel = 2
}
