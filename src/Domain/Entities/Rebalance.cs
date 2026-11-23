using MyWealthV2.Domain.Rebalances;

namespace MyWealthV2.Domain.Entities;

/// <summary>
/// One run that brings one assigned Custodial account towards the targets of its portfolio through orders
/// (docs/features/rebalances.md R2, §5): a proposal, a person's confirmation, then ordinary orders in waves. It is an
/// instruction, not a Transaction and not an order, and moves nothing in the ledger.
/// </summary>
public class Rebalance : BaseAuditableEntity
{
    public const string ExpiredMessage = "This proposal has expired. Propose the rebalance again.";
    public const string CancelAlreadyRequestedMessage = "A cancellation was already requested for this rebalance.";

    private readonly List<RebalanceTarget> _targets = [];
    private readonly List<RebalanceLine> _lines = [];

    private Rebalance()
    {
    }

    public Guid PublicId { get; private set; }

    public int TenantId { get; private set; }

    public int AccountId { get; private set; }

    public int PortfolioId { get; private set; }

    public int AssignmentId { get; private set; }

    public RebalanceTrigger Trigger { get; private set; }

    public RebalanceStatus Status { get; private set; }

    /// <summary>The portfolio's base currency when proposed.</summary>
    public string BaseCurrency { get; private set; } = string.Empty;

    /// <summary><c>V</c> of the plan, in the base currency; null on a <c>Failed</c> rebalance.</summary>
    public decimal? TotalValue { get; private set; }

    /// <summary>The available cash the plan was made with, in the account's currency; null on a <c>Failed</c>
    /// rebalance.</summary>
    public decimal? Cash { get; private set; }

    public decimal MinimumTrade { get; private set; }

    public decimal CashResidualPercent { get; private set; }

    /// <summary>The Tenant's broker connection when it was confirmed; every order of it takes this one (R13).</summary>
    public string? Connection { get; private set; }

    /// <summary>The waves placed (R14).</summary>
    public int Waves { get; private set; }

    /// <summary>The buy waves placed; at most <see cref="RebalanceRules.MaxBuyWaves"/>.</summary>
    public int BuyWaves { get; private set; }

    public string? FailureCode { get; private set; }

    public string? FailureMessage { get; private set; }

    /// <summary>Null for the job and a follow-up.</summary>
    public int? ProposedByUserId { get; private set; }

    public DateTimeOffset ProposedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public int? ConfirmedByUserId { get; private set; }

    public DateTimeOffset? ConfirmedAt { get; private set; }

    public DateTimeOffset? CancelRequestedAt { get; private set; }

    /// <summary>The person who asked; null for the platform's request (R22).</summary>
    public int? CancelRequestedByUserId { get; private set; }

    public string? CancelNote { get; private set; }

    public RebalanceCancelReason? CancelReason { get; private set; }

    /// <summary>R12: propose again when this one ends.</summary>
    public bool FollowUp { get; private set; }

    public DateTimeOffset? EndedAt { get; private set; }

    public byte[] RowVersion { get; private set; } = null!;

    /// <summary>The rebalance's own copy of the portfolio's lines (R9), in their order.</summary>
    public IReadOnlyList<RebalanceTarget> Targets => _targets;

    /// <summary>The lines of the proposal (R9); never changed.</summary>
    public IReadOnlyList<RebalanceLine> Lines => _lines;

    public bool IsOpen => throw new NotImplementedException();

    public bool IsInProgress => throw new NotImplementedException();

    public bool IsConfirmed => throw new NotImplementedException();

    public bool HasCancelRequest => throw new NotImplementedException();

    public static bool IsOpenStatus(RebalanceStatus status) => throw new NotImplementedException();

    public static bool IsInProgressStatus(RebalanceStatus status) => throw new NotImplementedException();

    public bool IsExpired(DateTimeOffset at) => throw new NotImplementedException();

    public static Rebalance Propose(
        RebalanceSubject subject, decimal totalValue, decimal cash, IReadOnlyList<RebalanceLineSpec> lines,
        DateTimeOffset at) => throw new NotImplementedException();

    public static Rebalance NoChange(RebalanceSubject subject, decimal totalValue, decimal cash, DateTimeOffset at) =>
        throw new NotImplementedException();

    public static Rebalance Fail(RebalanceSubject subject, string code, string message, DateTimeOffset at) =>
        throw new NotImplementedException();

    public void Confirm(int byUserId, string connection, DateTimeOffset at) => throw new NotImplementedException();

    public void EndNoChange(DateTimeOffset at) => throw new NotImplementedException();

    public void StartWave(OrderSide side) => throw new NotImplementedException();

    public void Complete(DateTimeOffset at) => throw new NotImplementedException();

    public void EndIncomplete(string code, string message, DateTimeOffset at) => throw new NotImplementedException();

    public void RequestCancel(int? byUserId, string? note, DateTimeOffset at) => throw new NotImplementedException();

    public void Cancel(
        RebalanceCancelReason reason, DateTimeOffset at, bool anyOrderLive, int? byUserId = null, string? note = null) =>
        throw new NotImplementedException();

    public void MarkFollowUp() => throw new NotImplementedException();
}
