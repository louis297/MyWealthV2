namespace MyWealthV2.Domain.Rebalances;

/// <summary>The fixed numbers of a rebalance (docs/features/rebalances.md R11, R14).</summary>
public static class RebalanceRules
{
    /// <summary>R11: a proposal can be confirmed for this many hours after it was made.</summary>
    public const int ProposalValidHours = 24;

    /// <summary>R14: the buys, and one top-up.</summary>
    public const int MaxBuyWaves = 2;

    public const int NoteMaxLength = 200;
    public const int FailureCodeMaxLength = 50;
    public const int FailureMessageMaxLength = 500;
}
