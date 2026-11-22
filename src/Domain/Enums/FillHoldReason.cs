namespace MyWealthV2.Domain.Enums;

/// <summary>Why a fill waits to be posted (orders R36).</summary>
public enum FillHoldReason
{
    /// <summary>The cash after the fill would be below the cash that stays reserved.</summary>
    InsufficientCash = 0,

    /// <summary>A sell's quantity is more than the holding.</summary>
    InsufficientHolding = 1,

    /// <summary>The Account is Closed.</summary>
    AccountClosed = 2,

    /// <summary>The FX rate a sell in another currency needs is missing.</summary>
    NoFxRate = 3
}
