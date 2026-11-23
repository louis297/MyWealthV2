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

    /// <summary>R3: <c>Proposed</c>, <c>Selling</c> or <c>Buying</c>.</summary>
    public bool IsOpen => IsOpenStatus(Status);

    /// <summary>R3: <c>Selling</c> or <c>Buying</c>.</summary>
    public bool IsInProgress => IsInProgressStatus(Status);

    public bool IsConfirmed => ConfirmedAt is not null;

    public bool HasCancelRequest => CancelRequestedAt is not null;

    public static bool IsOpenStatus(RebalanceStatus status) =>
        status is RebalanceStatus.Proposed or RebalanceStatus.Selling or RebalanceStatus.Buying;

    public static bool IsInProgressStatus(RebalanceStatus status) =>
        status is RebalanceStatus.Selling or RebalanceStatus.Buying;

    /// <summary>R11: a proposal can be confirmed until <see cref="ExpiresAt"/>.</summary>
    public bool IsExpired(DateTimeOffset at) => at >= ExpiresAt;

    /// <summary>R9: a plan with trades, stored as <c>Proposed</c> with its lines and own targets. It places no order
    /// and reserves nothing.</summary>
    public static Rebalance Propose(
        RebalanceSubject subject, decimal totalValue, decimal cash, IReadOnlyList<RebalanceLineSpec> lines,
        DateTimeOffset at)
    {
        if (!lines.Any(line => line.Trade is not null))
        {
            throw new DomainException("A proposal has at least one trade.");
        }

        if (lines.Select(line => line.InstrumentId).Distinct().Count() != lines.Count)
        {
            throw new DomainException("A proposal has one line per instrument.");
        }

        var rebalance = Create(subject, RebalanceStatus.Proposed, at);
        rebalance.TotalValue = totalValue;
        rebalance.Cash = cash;
        foreach (var line in lines)
        {
            rebalance._lines.Add(RebalanceLine.Create(subject.TenantId, line));
        }

        return rebalance;
    }

    /// <summary>R9: a plan with nothing to trade, stored as <c>NoChange</c> with no lines.</summary>
    public static Rebalance NoChange(RebalanceSubject subject, decimal totalValue, decimal cash, DateTimeOffset at)
    {
        var rebalance = Create(subject, RebalanceStatus.NoChange, at);
        rebalance.TotalValue = totalValue;
        rebalance.Cash = cash;
        rebalance.EndedAt = at;
        return rebalance;
    }

    /// <summary>R9: a plan that failed, stored as <c>Failed</c> with its code and message and no lines.</summary>
    public static Rebalance Fail(RebalanceSubject subject, string code, string message, DateTimeOffset at)
    {
        var rebalance = Create(subject, RebalanceStatus.Failed, at);
        rebalance.SetFailure(code, message);
        rebalance.EndedAt = at;
        return rebalance;
    }

    /// <summary>R13: a person confirms a <c>Proposed</c> rebalance that has not expired. The status moves with the
    /// first wave (<see cref="StartWave"/>) or <see cref="EndNoChange"/>.</summary>
    public void Confirm(int byUserId, string connection, DateTimeOffset at)
    {
        if (Status != RebalanceStatus.Proposed || IsConfirmed)
        {
            throw new RebalanceStatusException($"This rebalance is {Status} and cannot be confirmed.");
        }

        if (IsExpired(at))
        {
            throw new RebalanceStatusException(ExpiredMessage);
        }

        if (string.IsNullOrWhiteSpace(connection))
        {
            throw new DomainException("A rebalance is confirmed with a broker connection.");
        }

        ConfirmedByUserId = byUserId;
        ConfirmedAt = at;
        Connection = connection.Trim();
    }

    /// <summary>R13: the plan made again at confirm has nothing to trade.</summary>
    public void EndNoChange(DateTimeOffset at)
    {
        if (Status != RebalanceStatus.Proposed)
        {
            throw new RebalanceStatusException($"This rebalance is {Status} and cannot end with nothing to trade.");
        }

        Status = RebalanceStatus.NoChange;
        EndedAt = at;
    }

    /// <summary>R14: a sell wave only as the first wave of a confirmed rebalance; a buy wave after confirm, the sells
    /// or the first buys, at most <see cref="RebalanceRules.MaxBuyWaves"/> times; none after a cancel request.</summary>
    public void StartWave(OrderSide side)
    {
        if (!IsConfirmed || !IsOpen)
        {
            throw new RebalanceStatusException($"This rebalance is {Status} and cannot place a wave.");
        }

        if (HasCancelRequest)
        {
            throw new RebalanceStatusException("A cancellation was requested for this rebalance.");
        }

        if (side == OrderSide.Sell)
        {
            if (Status != RebalanceStatus.Proposed || Waves != 0)
            {
                throw new RebalanceStatusException("Sells are placed only in the first wave.");
            }

            Status = RebalanceStatus.Selling;
        }
        else
        {
            if (BuyWaves >= RebalanceRules.MaxBuyWaves)
            {
                throw new RebalanceStatusException($"A rebalance places at most {RebalanceRules.MaxBuyWaves} buy waves.");
            }

            Status = RebalanceStatus.Buying;
            BuyWaves++;
        }

        Waves++;
    }

    /// <summary>R17 (c), (e): every order was filled and there is nothing more to place.</summary>
    public void Complete(DateTimeOffset at)
    {
        EnsureInProgress("completed");
        Status = RebalanceStatus.Completed;
        EndedAt = at;
    }

    /// <summary>R17 (b), (d): it ended early, with a code and a message.</summary>
    public void EndIncomplete(string code, string message, DateTimeOffset at)
    {
        EnsureInProgress("ended");
        SetFailure(code, message);
        Status = RebalanceStatus.Incomplete;
        EndedAt = at;
    }

    /// <summary>R19, R22: a cancel request on a rebalance in progress; the status is unchanged until no order is
    /// live. <paramref name="byUserId"/> is null for the platform.</summary>
    public void RequestCancel(int? byUserId, string? note, DateTimeOffset at)
    {
        if (!IsInProgress)
        {
            throw new RebalanceStatusException($"This rebalance is {Status} and cannot be cancelled.");
        }

        if (HasCancelRequest)
        {
            throw new RebalanceStatusException(CancelAlreadyRequestedMessage);
        }

        CancelRequestedAt = at;
        CancelRequestedByUserId = byUserId;
        CancelNote = Note(note);
    }

    /// <summary>R4, R17 (a), R19, R22: a <c>Proposed</c> rebalance at once; one in progress only when none of its
    /// orders is live. A person's note on a proposal is kept with the request.</summary>
    public void Cancel(
        RebalanceCancelReason reason, DateTimeOffset at, bool anyOrderLive, int? byUserId = null, string? note = null)
    {
        if (!IsOpen)
        {
            throw new RebalanceStatusException($"This rebalance is {Status} and cannot be cancelled.");
        }

        if (IsInProgress && anyOrderLive)
        {
            throw new RebalanceStatusException("This rebalance has live orders and cannot be cancelled yet.");
        }

        if (!HasCancelRequest && (byUserId is not null || note is not null))
        {
            CancelRequestedAt = at;
            CancelRequestedByUserId = byUserId;
            CancelNote = Note(note);
        }

        Status = RebalanceStatus.Cancelled;
        CancelReason = reason;
        EndedAt = at;
    }

    /// <summary>R12: the job found this rebalance in progress; propose again when it ends.</summary>
    public void MarkFollowUp()
    {
        if (!IsInProgress)
        {
            throw new RebalanceStatusException($"This rebalance is {Status} and cannot take a follow-up.");
        }

        FollowUp = true;
    }

    private static Rebalance Create(RebalanceSubject subject, RebalanceStatus status, DateTimeOffset at)
    {
        if (subject.Targets.Count == 0)
        {
            throw new DomainException("A rebalance has at least one target.");
        }

        if (subject.Targets.Select(target => target.InstrumentId).Distinct().Count() != subject.Targets.Count)
        {
            throw new DomainException("A rebalance has one target per instrument.");
        }

        if (subject.MinimumTrade < 0m || subject.CashResidualPercent is < 0m or > 100m)
        {
            throw new DomainException("A rebalance's minimum trade and cash residual are not valid.");
        }

        var baseCurrency = subject.BaseCurrency?.Trim() ?? string.Empty;
        if (baseCurrency.Length != 3)
        {
            throw new DomainException("A rebalance has a 3-letter base currency.");
        }

        var rebalance = new Rebalance
        {
            PublicId = subject.PublicId is { } id && id != Guid.Empty ? id : Guid.NewGuid(),
            TenantId = subject.TenantId,
            AccountId = subject.AccountId,
            PortfolioId = subject.PortfolioId,
            AssignmentId = subject.AssignmentId,
            Trigger = subject.Trigger,
            Status = status,
            BaseCurrency = baseCurrency.ToUpperInvariant(),
            MinimumTrade = subject.MinimumTrade,
            CashResidualPercent = subject.CashResidualPercent,
            ProposedByUserId = subject.ProposedByUserId,
            ProposedAt = at,
            ExpiresAt = at.AddHours(RebalanceRules.ProposalValidHours)
        };

        var order = 0;
        foreach (var target in subject.Targets)
        {
            rebalance._targets.Add(RebalanceTarget.Create(subject.TenantId, target, order++));
        }

        return rebalance;
    }

    private void EnsureInProgress(string verb)
    {
        if (!IsInProgress)
        {
            throw new RebalanceStatusException($"This rebalance is {Status} and cannot be {verb}.");
        }
    }

    private void SetFailure(string code, string message)
    {
        var trimmedCode = code?.Trim() ?? string.Empty;
        if (trimmedCode.Length is 0 or > RebalanceRules.FailureCodeMaxLength)
        {
            throw new DomainException($"Failure code must be 1 to {RebalanceRules.FailureCodeMaxLength} characters.");
        }

        var trimmed = message?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            trimmed = trimmedCode;
        }

        FailureCode = trimmedCode;
        FailureMessage = trimmed.Length > RebalanceRules.FailureMessageMaxLength
            ? trimmed[..RebalanceRules.FailureMessageMaxLength]
            : trimmed;
    }

    private static string? Note(string? note)
    {
        if (note is null)
        {
            return null;
        }

        var trimmed = note.Trim();
        if (trimmed.Length is 0 or > RebalanceRules.NoteMaxLength)
        {
            throw new DomainException($"Note must be 1 to {RebalanceRules.NoteMaxLength} characters.");
        }

        return trimmed;
    }
}
