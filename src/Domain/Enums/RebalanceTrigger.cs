namespace MyWealthV2.Domain.Enums;

/// <summary>What started a rebalance (docs/features/rebalances.md R5).</summary>
public enum RebalanceTrigger
{
    /// <summary>A portfolio was assigned to the account.</summary>
    Assign = 0,

    /// <summary>A person asked for it on the account (<c>POST /accounts/{id}/rebalance</c>).</summary>
    Account = 1,

    /// <summary>The job, after a weight change or <c>POST /portfolios/{id}/rebalance</c>.</summary>
    Portfolio = 2,

    /// <summary>The platform, after a rebalance marked for a follow-up ended (R12).</summary>
    FollowUp = 3
}
