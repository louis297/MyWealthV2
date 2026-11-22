namespace MyWealthV2.Domain.Enums;

/// <summary>Why a call to a broker connection failed (orders R4, R21, R24). Never a message from the party.</summary>
public enum BrokerFailure
{
    Unreachable = 0,
    Timeout = 1,
    Unauthorized = 2,
    Unavailable = 3,
    BadResponse = 4,

    /// <summary>A page of the feed held an item the platform cannot store (R24). Never the failure of a send.</summary>
    InvalidItem = 5
}
