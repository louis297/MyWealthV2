namespace MyWealthV2.Domain.Enums;

/// <summary>In-kind-transfers R8, R15: what moved the custodian's position. A kind the adapter does not know is stored
/// as no kind.</summary>
public enum CustodyMovementKind
{
    Transfer = 0,
    Trade = 1,
    CorporateAction = 2
}
