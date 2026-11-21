using MyWealthV2.Domain.ValueObjects;

namespace MyWealthV2.Domain.Entities;

/// <summary>
/// A bank reconciliation of one client money account at one instant (bank-reconciliation R6–R15, §5): the bank side,
/// the platform side and every item between them. Built once by <see cref="Compute"/> and never changed (R15).
/// </summary>
public class BankReconciliation : BaseAuditableEntity
{
    private readonly List<BankReconciliationItem> _items = [];

    private BankReconciliation()
    {
    }

    public Guid PublicId { get; private set; }

    public int TenantId { get; private set; }

    public int ClientMoneyAccountId { get; private set; }

    public string Currency { get; private set; } = null!;

    public ReconciliationTrigger Trigger { get; private set; }

    public DateTimeOffset RanAt { get; private set; }

    public int? RanByUserId { get; private set; }

    public BankReconciliationStatus Status { get; private set; }

    public decimal? BankBalance { get; private set; }

    public DateTimeOffset? BalanceAt { get; private set; }

    public decimal? OpeningBalance { get; private set; }

    public decimal? StatementLinesTotal { get; private set; }

    public decimal CustodialCash { get; private set; }

    /// <summary><c>BankBalance − CustodialCash</c> (R8).</summary>
    public decimal? Difference { get; private set; }

    public int BreakCount { get; private set; }

    public decimal BreakTotal { get; private set; }

    public int TimingCount { get; private set; }

    public decimal TimingTotal { get; private set; }

    public int KnownCount { get; private set; }

    public decimal KnownTotal { get; private set; }

    public decimal ReservedCash { get; private set; }

    public int PaymentsInFlightCount { get; private set; }

    public decimal PaymentsInFlightAmount { get; private set; }

    public DateTimeOffset? FeedLastSucceededAt { get; private set; }

    public DateTimeOffset? FeedLastFailedAt { get; private set; }

    public BankFeedFailure? FeedLastFailure { get; private set; }

    /// <summary>The items in R13 order.</summary>
    public IReadOnlyList<BankReconciliationItem> Items => _items;

    /// <summary>
    /// Builds the run from what was read (R9–R13): the items, the status and the totals. A <c>Scheduled</c> run names
    /// no person; a <c>Manual</c> run names <paramref name="ranByUserId"/>.
    /// </summary>
    public static BankReconciliation Compute(
        ReconciliationInputs inputs,
        ReconciliationTrigger trigger,
        int? ranByUserId,
        DateTimeOffset at)
    {
        throw new NotImplementedException();
    }
}
