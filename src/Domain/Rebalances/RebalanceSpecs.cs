namespace MyWealthV2.Domain.Rebalances;

/// <summary>What a rebalance is made against (docs/features/rebalances.md R5, R9): the account and its assignment,
/// the trigger, the person who asked (null for the job and a follow-up), and the rebalance's own targets and
/// settings, copied from the portfolio.</summary>
public sealed record RebalanceSubject(
    int TenantId,
    int AccountId,
    int PortfolioId,
    int AssignmentId,
    RebalanceTrigger Trigger,
    string BaseCurrency,
    decimal MinimumTrade,
    decimal CashResidualPercent,
    IReadOnlyList<RebalanceTargetSpec> Targets,
    int? ProposedByUserId,
    Guid? PublicId = null);

/// <summary>One target line of a rebalance's own targets (R9).</summary>
public sealed record RebalanceTargetSpec(int InstrumentId, decimal TargetWeight);

/// <summary>One line of a proposal (R9): an Instrument held or targeted, and the trade the plan would make.</summary>
public sealed record RebalanceLineSpec(
    int InstrumentId,
    decimal Quantity,
    decimal Weight,
    decimal? TargetWeight,
    string Currency,
    RebalanceLineTrade? Trade);

/// <summary>The trade of a proposal line at the stored price (R9): an estimate.</summary>
public sealed record RebalanceLineTrade(
    OrderSide Side,
    decimal Quantity,
    decimal ReferencePrice,
    DateOnly ReferencePriceDate,
    decimal EstimatedGross,
    decimal EstimatedFee);
