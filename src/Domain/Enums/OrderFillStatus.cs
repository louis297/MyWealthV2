namespace MyWealthV2.Domain.Enums;

/// <summary>Where a fill is (orders R26): <c>Posted</c> once its Transaction exists, <c>Pending</c> until then.</summary>
public enum OrderFillStatus
{
    Pending = 0,
    Posted = 1,

    /// <summary>The broker took the fill back (trade-settlement R27).</summary>
    Broken = 2
}
