namespace MyWealthV2.Domain.Enums;

/// <summary>Why a read of a custodian feed or a send of a transfer instruction failed (in-kind-transfers R7, R35, R73).
/// The send step never records <see cref="InvalidItem"/>.</summary>
public enum CustodianFailure
{
    Unreachable = 0,
    Timeout = 1,
    Unauthorized = 2,
    Unavailable = 3,
    UnknownAccount = 4,
    BadResponse = 5,
    InvalidItem = 6
}
