namespace MyWealthV2.Domain.Enums;

/// <summary>Where a rebalance is (docs/features/rebalances.md R3). Open while <c>Proposed</c>, <c>Selling</c> or
/// <c>Buying</c>; in progress while <c>Selling</c> or <c>Buying</c>; every other status is final.</summary>
public enum RebalanceStatus
{
    /// <summary>The plan is made and waits for a person.</summary>
    Proposed = 0,

    /// <summary>Confirmed; the sell orders of the first wave are live.</summary>
    Selling = 1,

    /// <summary>A wave of buy orders is live.</summary>
    Buying = 2,

    /// <summary>Every order it placed was filled in full, and it has nothing more to place.</summary>
    Completed = 3,

    /// <summary>It ended early: an order was not filled in full, or a wave could not be planned.</summary>
    Incomplete = 4,

    /// <summary>A person cancelled it, a newer proposal replaced it, or its assignment ended.</summary>
    Cancelled = 5,

    /// <summary>The plan found nothing to trade.</summary>
    NoChange = 6,

    /// <summary>The proposal could not be planned.</summary>
    Failed = 7
}
