using System.Data;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.ValueObjects;

namespace MyWealthV2.Application.CustodianReconciliations;

/// <summary>Runs one custodian reconciliation of a safekeeping account (in-kind-transfers R50–R56).</summary>
public interface IReconcileSafekeepingAccount
{
    /// <summary>Reads the inputs at SNAPSHOT, computes the run and stores it; answers the run's PublicId.</summary>
    Task<Guid> RunAsync(int safekeepingAccountId, ReconciliationTrigger trigger, int? ranByUserId, CancellationToken cancellationToken);
}

/// <summary>
/// R50: reads local tables only, in one SNAPSHOT transaction across the Tenant of the safekeeping account, and stores
/// the run in its own transaction. Every read names the account's Tenant, so the service runs the same on
/// <c>webapi</c> and on <c>worker</c>.
/// </summary>
public sealed class ReconcileSafekeepingAccount(IApplicationDbContext db, TimeProvider time) : IReconcileSafekeepingAccount
{
    public async Task<Guid> RunAsync(int safekeepingAccountId, ReconciliationTrigger trigger, int? ranByUserId, CancellationToken cancellationToken)
    {
        db.ClearTrackedChanges();
        CustodianReconciliationInputs? inputs = null;
        var at = time.GetUtcNow();
        await db.ExecuteInTransactionAsync(IsolationLevel.Snapshot, async token =>
        {
            at = time.GetUtcNow();
            inputs = await ReadAsync(safekeepingAccountId, token);
        }, cancellationToken);

        var run = CustodianReconciliation.Compute(inputs!, trigger, ranByUserId, at);
        run.CreatedBy = SystemActor.Name;
        db.CustodianReconciliations.Add(run);
        await db.SaveChangesAsync(cancellationToken);
        return run.PublicId;
    }

    private async Task<CustodianReconciliationInputs> ReadAsync(int safekeepingAccountId, CancellationToken cancellationToken)
    {
        var account = await db.SafekeepingAccounts.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(row => row.Id == safekeepingAccountId, cancellationToken);
        var tenantId = account.TenantId;
        var currency = account.Currency;
        var cursor = await db.CustodianFeedCursors.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(row => row.SafekeepingAccountId == account.Id, cancellationToken);
        var positions = await db.CustodianPositions.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.SafekeepingAccountId == account.Id)
            .Select(row => new CustodianSymbolQuantity(row.Symbol, row.Quantity))
            .ToListAsync(cancellationToken);

        // R51: every Instrument of the Tenant quoted in the account's currency, and the sum of its legs on every
        // Custodial account of the Tenant, whatever the account's status and currency.
        var instruments = await db.Instruments.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.TenantId == tenantId && row.QuoteCurrency == currency)
            .Select(row => new { row.Id, row.Symbol })
            .ToListAsync(cancellationToken);
        var instrumentIds = instruments.Select(row => row.Id).ToList();
        var legs = await (
                from leg in db.TransactionSecurityLegs.IgnoreQueryFilters()
                join holder in db.Accounts.IgnoreQueryFilters() on leg.AccountId equals holder.Id
                where holder.TenantId == tenantId && holder.CustodyMode == CustodyMode.Custodial && instrumentIds.Contains(leg.InstrumentId)
                group leg by leg.InstrumentId into grouped
                select new { InstrumentId = grouped.Key, Quantity = grouped.Sum(leg => leg.Quantity) })
            .ToDictionaryAsync(row => row.InstrumentId, row => row.Quantity, cancellationToken);
        var platform = instruments
            .Select(row => new CustodianInstrumentQuantity(row.Id, row.Symbol, legs.GetValueOrDefault(row.Id)))
            .ToList();

        // R53: the movements of the account that the platform has not placed yet, or that wait in suspense.
        var movements = await db.CustodyMovements.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.SafekeepingAccountId == account.Id
                          && (row.Status == CustodyMovementStatus.Received || row.Status == CustodyMovementStatus.Unmatched))
            .Select(row => new { row.Status, row.Symbol, row.Direction, row.Quantity })
            .ToListAsync(cancellationToken);
        List<CustodianSymbolQuantity> Signed(CustodyMovementStatus status) => movements
            .Where(row => row.Status == status)
            .Select(row => new CustodianSymbolQuantity(row.Symbol, row.Direction == CustodyMovementDirection.In ? row.Quantity : -row.Quantity))
            .ToList();

        // R53, R55: the fills of the Tenant's orders that carry the account, delivered or not.
        var fills = await (
                from fill in db.OrderFills.IgnoreQueryFilters()
                join order in db.Orders.IgnoreQueryFilters() on fill.OrderId equals order.Id
                where order.TenantId == tenantId && order.SafekeepingAccountId == account.Id
                      && (fill.Status == OrderFillStatus.Posted || fill.Status == OrderFillStatus.Pending)
                select new
                {
                    fill.Status,
                    order.InstrumentId,
                    Signed = order.Side == OrderSide.Buy ? fill.Quantity : -fill.Quantity,
                    Delivered = db.CustodyMovements.IgnoreQueryFilters().Any(movement => movement.OrderFillId == fill.Id)
                })
            .ToListAsync(cancellationToken);
        var symbols = instruments.ToDictionary(row => row.Id, row => row.Symbol);
        var notDelivered = fills.Where(row => row.Status == OrderFillStatus.Posted && !row.Delivered).ToList();
        var deliveredPending = fills.Where(row => row.Status == OrderFillStatus.Pending && row.Delivered).ToList();

        var inFlight = await db.InKindTransfers.IgnoreQueryFilters()
            .CountAsync(row => row.SafekeepingAccountId == account.Id
                               && (row.Status == InKindTransferStatus.Sent
                                   || (row.Status == InKindTransferStatus.Approved && row.Attempts > 0)), cancellationToken);

        return new CustodianReconciliationInputs
        {
            TenantId = tenantId,
            SafekeepingAccountId = account.Id,
            Currency = currency,
            PositionsAt = cursor?.PositionsAt,
            Positions = positions,
            Platform = platform,
            NotProcessed = Signed(CustodyMovementStatus.Received),
            Suspense = Signed(CustodyMovementStatus.Unmatched),
            FillsNotDelivered = notDelivered
                .Select(row => new CustodianInstrumentQuantity(row.InstrumentId, symbols.GetValueOrDefault(row.InstrumentId, ""), row.Signed))
                .ToList(),
            DeliveredPendingFills = deliveredPending
                .Select(row => new CustodianInstrumentQuantity(row.InstrumentId, symbols.GetValueOrDefault(row.InstrumentId, ""), row.Signed))
                .ToList(),
            TransfersInFlight = inFlight,
            FillsNotDeliveredCount = notDelivered.Count,
            FeedLastSucceededAt = cursor?.LastSucceededAt,
            FeedLastFailedAt = cursor?.LastFailedAt,
            FeedLastFailure = cursor?.LastFailure
        };
    }
}
