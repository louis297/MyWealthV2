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
        CustodianReconciliationInputs inputs, ReconciliationTrigger trigger, int? ranByUserId, DateTimeOffset at)
    {
        if (trigger == ReconciliationTrigger.Scheduled && ranByUserId is not null)
        {
            throw new DomainException("A scheduled run names no person.");
        }

        if (trigger == ReconciliationTrigger.Manual && ranByUserId is null)
        {
            throw new DomainException("A manual run names the person who ran it.");
        }

        var run = new CustodianReconciliation
        {
            PublicId = Guid.NewGuid(),
            TenantId = inputs.TenantId,
            SafekeepingAccountId = inputs.SafekeepingAccountId,
            Currency = inputs.Currency.Trim().ToUpperInvariant(),
            Trigger = trigger,
            RanAt = at,
            RanByUserId = ranByUserId,
            PositionsAt = inputs.PositionsAt,
            TransfersInFlight = inputs.TransfersInFlight,
            FillsNotDelivered = inputs.FillsNotDeliveredCount,
            FeedLastSucceededAt = inputs.FeedLastSucceededAt,
            FeedLastFailedAt = inputs.FeedLastFailedAt,
            FeedLastFailure = inputs.FeedLastFailure
        };

        // R54: with no stored positions there is nothing to compare.
        if (inputs.PositionsAt is null)
        {
            run.Status = CustodianReconciliationStatus.NoPositions;
            return run;
        }

        var comparer = StringComparer.OrdinalIgnoreCase;
        var instruments = inputs.Platform
            .GroupBy(entry => entry.Symbol.Trim(), comparer)
            .ToDictionary(group => group.Key, group => group.First(), comparer);
        var bySymbol = (IEnumerable<CustodianSymbolQuantity> entries) => entries
            .GroupBy(entry => entry.Symbol.Trim(), comparer)
            .ToDictionary(group => group.Key, group => group.Sum(entry => entry.Quantity), comparer);
        var byInstrument = (IEnumerable<CustodianInstrumentQuantity> entries) => entries
            .GroupBy(entry => entry.InstrumentId)
            .ToDictionary(group => group.Key, group => group.Sum(entry => entry.Quantity));
        var positions = bySymbol(inputs.Positions);
        var notProcessed = bySymbol(inputs.NotProcessed);
        var suspense = bySymbol(inputs.Suspense);
        var notDelivered = byInstrument(inputs.FillsNotDelivered);
        var delivered = byInstrument(inputs.DeliveredPendingFills);

        // R52: one line per symbol of a position, a movement or an Instrument, compared without regard to case.
        var symbols = positions.Keys.Concat(notProcessed.Keys).Concat(suspense.Keys).Concat(instruments.Keys)
            .Distinct(comparer)
            .ToList();
        var lines = new List<CustodianReconciliationLine>();
        foreach (var symbol in symbols)
        {
            var instrument = instruments.GetValueOrDefault(symbol);
            var line = new CustodianReconciliationLine
            {
                TenantId = inputs.TenantId,
                Symbol = instrument?.Symbol.Trim() ?? symbol,
                InstrumentId = instrument?.InstrumentId,
                CustodianQuantity = positions.GetValueOrDefault(symbol),
                PlatformQuantity = instrument?.Quantity ?? 0m,
                NotProcessed = notProcessed.GetValueOrDefault(symbol),
                UnsettledTrades = instrument is null ? 0m : -notDelivered.GetValueOrDefault(instrument.InstrumentId),
                Suspense = suspense.GetValueOrDefault(symbol),
                UnbookedFills = instrument is null ? 0m : delivered.GetValueOrDefault(instrument.InstrumentId)
            };
            line.Difference = line.CustodianQuantity - line.PlatformQuantity;
            line.Unexplained = line.Difference - line.NotProcessed - line.UnsettledTrades - line.Suspense - line.UnbookedFills;
            if (line.CustodianQuantity == 0m && line.PlatformQuantity == 0m && line.NotProcessed == 0m && line.UnsettledTrades == 0m
                && line.Suspense == 0m && line.UnbookedFills == 0m && line.Unexplained == 0m)
            {
                continue;
            }

            // R54: a break first, then timing, then agreed.
            line.Class = line.Suspense != 0m || line.UnbookedFills != 0m || line.Unexplained != 0m
                ? CustodianLineClass.Break
                : line.NotProcessed != 0m || line.UnsettledTrades != 0m
                    ? CustodianLineClass.Timing
                    : CustodianLineClass.Agreed;
            lines.Add(line);
        }

        var position = 1;
        foreach (var line in lines.OrderBy(line => line.Class).ThenBy(line => line.Symbol, StringComparer.OrdinalIgnoreCase))
        {
            line.Position = position++;
            run._lines.Add(line);
        }

        run.BreakCount = lines.Count(line => line.Class == CustodianLineClass.Break);
        run.TimingCount = lines.Count(line => line.Class == CustodianLineClass.Timing);
        run.AgreedCount = lines.Count(line => line.Class == CustodianLineClass.Agreed);
        run.Status = run.BreakCount > 0 ? CustodianReconciliationStatus.Breaks : CustodianReconciliationStatus.Agreed;
        return run;
    }
}

/// <summary>One symbol of a custodian reconciliation (R52–R54).</summary>
public class CustodianReconciliationLine
{
    internal CustodianReconciliationLine()
    {
    }

    public int Id { get; private set; }

    public int TenantId { get; internal set; }

    public int ReconciliationId { get; private set; }

    /// <summary>The R54 order, from 1.</summary>
    public int Position { get; internal set; }

    public string Symbol { get; internal set; } = string.Empty;

    public int? InstrumentId { get; internal set; }

    public decimal CustodianQuantity { get; internal set; }

    public decimal PlatformQuantity { get; internal set; }

    public decimal Difference { get; internal set; }

    public decimal NotProcessed { get; internal set; }

    public decimal UnsettledTrades { get; internal set; }

    public decimal Suspense { get; internal set; }

    public decimal UnbookedFills { get; internal set; }

    public decimal Unexplained { get; internal set; }

    public CustodianLineClass Class { get; internal set; }
}
