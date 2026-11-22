namespace MyWealthV2.Domain.Enums;

/// <summary>Why the last read of a market data feed failed (docs/features/simulated-instruments.md R5, R8, R25).</summary>
public enum MarketDataFailure
{
    Unreachable = 0,
    Timeout = 1,
    Unauthorized = 2,
    Unavailable = 3,
    BadResponse = 4,

    /// <summary>A page held a record the platform cannot store; nothing of it was stored (corporate-actions R9).</summary>
    InvalidItem = 5
}
