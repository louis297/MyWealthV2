namespace MyWealthV2.Domain.Valuations;

/// <summary>Whether one currency of an FX fold has a value in the reporting currency (valuation-history R10).</summary>
public enum FoldLineStatus
{
    Folded = 0,
    NoRate = 1,
    StaleRate = 2
}
