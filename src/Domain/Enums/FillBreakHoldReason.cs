namespace MyWealthV2.Domain.Enums;

/// <summary>Why a broken fill's Transaction could not be reversed (trade-settlement R30), in the order tried.</summary>
public enum FillBreakHoldReason
{
    /// <summary>The Account is Closed.</summary>
    AccountClosed = 0,

    /// <summary>The fill's Transaction is not the latest security activity on its holding.</summary>
    LaterActivity = 1,

    /// <summary>A broken sell whose Reversal would leave available cash below 0.</summary>
    InsufficientCash = 2
}
