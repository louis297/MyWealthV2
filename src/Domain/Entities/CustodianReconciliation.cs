using MyWealthV2.Domain.ValueObjects;

namespace MyWealthV2.Domain.Entities;

/// <summary>
/// A custodian reconciliation of one safekeeping account at one instant (docs/features/in-kind-transfers.md R50–R56):
/// one line per symbol with the custodian's quantity, the platform's and five components that add up to the
/// difference. Built once by <see cref="Compute"/> and never changed.
/// </summary>
public class CustodianReconciliation : BaseAuditableEntity
{
    private readonly List<CustodianReconciliationLine> _lines = [];

    private CustodianReconciliation()
    {
    }

    public Guid PublicId { get; private set; }

    public int TenantId { get; private set; }

    public int SafekeepingAccountId { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    public ReconciliationTrigger Trigger { get; private set; }

    public DateTimeOffset RanAt { get; private set; }

    public int? RanByUserId { get; private set; }

    public CustodianReconciliationStatus Status { get; private set; }

    public DateTimeOffset? PositionsAt { get; private set; }

    public int BreakCount { get; private set; }

    public int TimingCount { get; private set; }

    public int AgreedCount { get; private set; }

    public int TransfersInFlight { get; private set; }

    public int FillsNotDelivered { get; private set; }

    public DateTimeOffset? FeedLastSucceededAt { get; private set; }

    public DateTimeOffset? FeedLastFailedAt { get; private set; }

    public CustodianFailure? FeedLastFailure { get; private set; }

    /// <summary>The lines in R54 order.</summary>
    public IReadOnlyList<CustodianReconciliationLine> Lines => _lines;

    /// <summary>R51–R55: the lines, their components and classes, the status and the counts.</summary>
    public static CustodianReconciliation Compute(
        CustodianReconciliationInputs inputs, ReconciliationTrigger trigger, int? ranByUserId, DateTimeOffset at) =>
        throw new NotImplementedException();
}

/// <summary>One symbol of a custodian reconciliation (R52–R54).</summary>
public class CustodianReconciliationLine
{
    private CustodianReconciliationLine()
    {
    }

    public int Id { get; private set; }

    public int TenantId { get; private set; }

    public int ReconciliationId { get; private set; }

    /// <summary>The R54 order, from 1.</summary>
    public int Position { get; private set; }

    public string Symbol { get; private set; } = string.Empty;

    public int? InstrumentId { get; private set; }

    public decimal CustodianQuantity { get; private set; }

    public decimal PlatformQuantity { get; private set; }

    public decimal Difference { get; private set; }

    public decimal NotProcessed { get; private set; }

    public decimal UnsettledTrades { get; private set; }

    public decimal Suspense { get; private set; }

    public decimal UnbookedFills { get; private set; }

    public decimal Unexplained { get; private set; }

    public CustodianLineClass Class { get; private set; }
}
