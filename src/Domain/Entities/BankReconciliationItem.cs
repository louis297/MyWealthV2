using MyWealthV2.Domain.ValueObjects;

namespace MyWealthV2.Domain.Entities;

/// <summary>
/// One reconciliation item of a run (bank-reconciliation R9–R11, §5): what it explains, its class, its effect on bank
/// minus platform, and for a line item the line as it was at the run. Written once with its run and never changed.
/// </summary>
public class BankReconciliationItem
{
    private BankReconciliationItem()
    {
    }

    public int Id { get; private set; }

    public int TenantId { get; private set; }

    public int ReconciliationId { get; private set; }

    /// <summary>The R13 order, from 1.</summary>
    public int Position { get; private set; }

    public ReconciliationItemKind Kind { get; private set; }

    public ReconciliationItemClass Class { get; private set; }

    /// <summary>Signed: its effect on bank balance minus Custodial cash.</summary>
    public decimal Amount { get; private set; }

    /// <summary><c>1</c> for a line item and a check; the number of Transactions for a ledger item.</summary>
    public int Count { get; private set; }

    public int? StatementLineId { get; private set; }

    public BankLineDirection? Direction { get; private set; }

    public DateOnly? ValueDate { get; private set; }

    public BankStatementLineStatus? LineStatus { get; private set; }

    public UnmatchedReason? UnmatchedReason { get; private set; }

    public string? Note { get; private set; }

    public int? OtherClientMoneyAccountId { get; private set; }

    /// <summary>The one class of each kind (R9–R11).</summary>
    public static ReconciliationItemClass ClassOf(ReconciliationItemKind kind) => kind switch
    {
        ReconciliationItemKind.Unexplained or ReconciliationItemKind.StatementDifference or ReconciliationItemKind.Suspense
            or ReconciliationItemKind.TradesNeedingAction or ReconciliationItemKind.UnbookedFills
            => ReconciliationItemClass.Break,
        ReconciliationItemKind.NotProcessed or ReconciliationItemKind.ReturnInProgress or ReconciliationItemKind.UnsettledTrades
            or ReconciliationItemKind.MigrationNotPosted
            => ReconciliationItemClass.Timing,
        _ => ReconciliationItemClass.Known
    };

    internal static BankReconciliationItem Create(
        int tenantId,
        ReconciliationItemKind kind,
        decimal amount,
        int count,
        ReconciliationLine? line = null,
        int? otherClientMoneyAccountId = null) => new()
    {
        TenantId = tenantId,
        Kind = kind,
        Class = ClassOf(kind),
        Amount = amount,
        Count = count,
        StatementLineId = line?.Id,
        Direction = line?.Direction,
        ValueDate = line?.ValueDate,
        LineStatus = line?.Status,
        UnmatchedReason = line?.Status == BankStatementLineStatus.Unmatched ? line.UnmatchedReason : null,
        Note = line?.Note,
        OtherClientMoneyAccountId = otherClientMoneyAccountId
    };

    internal void PlaceAt(int position) => Position = position;
}
