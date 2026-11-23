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

    /// <summary>Trade-settlement R25: the open trade settlements of the account at the run, as information.</summary>
    public int OpenSettlementsCount { get; private set; }

    public decimal OpenSettlementsAmount { get; private set; }

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
        if ((trigger == ReconciliationTrigger.Scheduled) != (ranByUserId is null))
        {
            throw new DomainException("A scheduled reconciliation names no person; a manual one names who ran it.");
        }

        var run = new BankReconciliation
        {
            PublicId = Guid.NewGuid(),
            TenantId = inputs.TenantId,
            ClientMoneyAccountId = inputs.ClientMoneyAccountId,
            Currency = inputs.Currency,
            Trigger = trigger,
            RanAt = at,
            RanByUserId = ranByUserId,
            CustodialCash = inputs.CustodialCash,
            ReservedCash = inputs.ReservedCash,
            PaymentsInFlightCount = inputs.PaymentsInFlightCount,
            PaymentsInFlightAmount = inputs.PaymentsInFlightAmount,
            FeedLastSucceededAt = inputs.FeedLastSucceededAt,
            FeedLastFailedAt = inputs.FeedLastFailedAt,
            FeedLastFailure = inputs.FeedLastFailure
        };

        // R12: without a statement balance there is no bank side to compare.
        if (inputs.StatementBalance is not decimal bank)
        {
            run.Status = BankReconciliationStatus.NoBankBalance;
            return run;
        }

        var opening = inputs.OpeningBalance
                      ?? throw new DomainException("A statement balance comes with an opening balance (R4).");
        var difference = bank - inputs.CustodialCash;
        run.BankBalance = bank;
        run.BalanceAt = inputs.BalanceAt ?? throw new DomainException("A statement balance comes with its time (R3).");
        run.OpeningBalance = opening;
        run.StatementLinesTotal = inputs.StatementLinesTotal;
        run.Difference = difference;

        var items = new List<BankReconciliationItem>();
        items.AddRange(LineItems(inputs));
        items.AddRange(LedgerItems(inputs));
        AddUnlessZero(items, inputs.TenantId, ReconciliationItemKind.InterestCredited, -inputs.InterestCredited,
            inputs.InterestCreditedCount);
        AddUnlessZero(items, inputs.TenantId, ReconciliationItemKind.TaxWithheld, -inputs.TaxWithheld, inputs.TaxWithheldCount);
        AddUnlessZero(items, inputs.TenantId, ReconciliationItemKind.OpeningBalance, opening);
        AddUnlessZero(items, inputs.TenantId, ReconciliationItemKind.StatementDifference,
            bank - opening - inputs.StatementLinesTotal);
        AddUnlessZero(items, inputs.TenantId, ReconciliationItemKind.Unexplained,
            difference - items.Sum(item => item.Amount));

        // R13: by class, then kind, then line items by value date and line, ledger items by account.
        var ordered = items
            .OrderBy(item => item.Class)
            .ThenBy(item => item.Kind)
            .ThenBy(item => item.ValueDate)
            .ThenBy(item => item.StatementLineId)
            .ThenBy(item => item.OtherClientMoneyAccountId)
            .ToList();
        for (var index = 0; index < ordered.Count; index++)
        {
            ordered[index].PlaceAt(index + 1);
        }

        run._items.AddRange(ordered);
        run.BreakCount = Count(ordered, ReconciliationItemClass.Break);
        run.BreakTotal = Total(ordered, ReconciliationItemClass.Break);
        run.TimingCount = Count(ordered, ReconciliationItemClass.Timing);
        run.TimingTotal = Total(ordered, ReconciliationItemClass.Timing);
        run.KnownCount = Count(ordered, ReconciliationItemClass.Known);
        run.KnownTotal = Total(ordered, ReconciliationItemClass.Known);
        run.Status = run.BreakCount > 0 ? BankReconciliationStatus.Breaks : BankReconciliationStatus.Agreed;
        return run;
    }

    /// <summary>R9: one item per line in suspense, not processed, being returned or ignored.</summary>
    private static IEnumerable<BankReconciliationItem> LineItems(ReconciliationInputs inputs)
    {
        foreach (var line in inputs.Lines)
        {
            ReconciliationItemKind? kind = line.Status switch
            {
                BankStatementLineStatus.Unmatched => ReconciliationItemKind.Suspense,
                BankStatementLineStatus.Received => ReconciliationItemKind.NotProcessed,
                BankStatementLineStatus.ReturnRequested or BankStatementLineStatus.ReturnSent => ReconciliationItemKind.ReturnInProgress,
                // Interest-run R41: the bank's own interest, set aside, is its own known item.
                BankStatementLineStatus.Ignored when line.Kind == StatementLineKind.Interest
                                                     && line.Direction == BankLineDirection.Credit => ReconciliationItemKind.BankInterest,
                BankStatementLineStatus.Ignored => ReconciliationItemKind.Ignored,
                _ => null
            };
            if (kind is not null)
            {
                var amount = line.Direction == BankLineDirection.Credit ? line.Amount : -line.Amount;
                yield return BankReconciliationItem.Create(inputs.TenantId, kind.Value, amount, 1, line);
            }
        }
    }

    /// <summary>
    /// R10: ledger cash through this account makes no item; through another account, one item per account; any
    /// other by the type of its Transaction (a Reversal's original). A group that sums to zero is left out.
    /// </summary>
    private static IEnumerable<BankReconciliationItem> LedgerItems(ReconciliationInputs inputs) =>
        inputs.Ledger
            .Where(cash => cash.ThroughClientMoneyAccountId != inputs.ClientMoneyAccountId)
            .GroupBy(cash => cash.ThroughClientMoneyAccountId is int other
                ? (ReconciliationItemKind.OtherClientMoneyAccount, (int?)other)
                : (LedgerKind(cash.Type == TransactionType.Reversal ? cash.OriginalType ?? cash.Type : cash.Type), null))
            .Where(group => group.Sum(cash => cash.Sum) != 0m)
            .Select(group => BankReconciliationItem.Create(
                inputs.TenantId,
                group.Key.Item1,
                -group.Sum(cash => cash.Sum),
                group.Sum(cash => cash.Count),
                otherClientMoneyAccountId: group.Key.Item2));

    private static ReconciliationItemKind LedgerKind(TransactionType type) => type switch
    {
        TransactionType.Buy or TransactionType.Sell => ReconciliationItemKind.Trades,
        TransactionType.FxConversion => ReconciliationItemKind.FxConversions,
        TransactionType.Adjustment => ReconciliationItemKind.Adjustments,
        _ => ReconciliationItemKind.OtherLedgerCash
    };

    /// <summary>R11: a check whose amount is zero is left out.</summary>
    private static void AddUnlessZero(
        List<BankReconciliationItem> items,
        int tenantId,
        ReconciliationItemKind kind,
        decimal amount,
        int count = 1)
    {
        if (amount != 0m)
        {
            items.Add(BankReconciliationItem.Create(tenantId, kind, amount, Math.Max(count, 1)));
        }
    }

    private static int Count(List<BankReconciliationItem> items, ReconciliationItemClass itemClass) =>
        items.Count(item => item.Class == itemClass);

    private static decimal Total(List<BankReconciliationItem> items, ReconciliationItemClass itemClass) =>
        items.Where(item => item.Class == itemClass).Sum(item => item.Amount);
}
