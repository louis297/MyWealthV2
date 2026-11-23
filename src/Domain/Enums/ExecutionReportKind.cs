namespace MyWealthV2.Domain.Enums;

/// <summary>What an execution report says about its order (orders R23).</summary>
public enum ExecutionReportKind
{
    /// <summary>A quantity filled at a price, with trade date, settlement date and the broker's contract note.</summary>
    Fill = 0,

    /// <summary>The rest of the order ended: cancelled.</summary>
    Cancelled = 1,

    /// <summary>The rest of the order ended at the end of the broker's trading day.</summary>
    Expired = 2,

    /// <summary>The fill named by its outside id has settled.</summary>
    Settled = 3,

    /// <summary>The broker takes back the fill named by its outside id (trade-settlement R26).</summary>
    Broken = 4,

    /// <summary>One broker settlement is closed, with its net and its number of fills (trade-settlement R7).</summary>
    Settlement = 5
}
