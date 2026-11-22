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
    Settled = 3
}
