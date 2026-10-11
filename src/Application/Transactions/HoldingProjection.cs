using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Application.Transactions;

internal static class HoldingProjection
{
    public static async Task SyncAsync(
        IApplicationDbContext db,
        int tenantId,
        int accountId,
        IEnumerable<int> instrumentIds,
        CancellationToken cancellationToken)
    {
        foreach (var instrumentId in instrumentIds.Distinct())
        {
            var legs = await (
                from leg in db.TransactionSecurityLegs
                join transaction in db.Transactions on leg.TransactionId equals transaction.Id
                where transaction.AccountId == accountId && leg.InstrumentId == instrumentId
                select new { leg.Quantity, leg.CostAmount, leg.CostCurrency }).ToListAsync(cancellationToken);

            var quantity = legs.Sum(leg => leg.Quantity);
            var cost = legs.Sum(leg => leg.CostAmount);
            var currency = legs.FirstOrDefault()?.CostCurrency ?? string.Empty;
            var holding = await db.Holdings.SingleOrDefaultAsync(
                row => row.AccountId == accountId && row.InstrumentId == instrumentId,
                cancellationToken);

            if (quantity == 0m)
            {
                if (holding is not null)
                {
                    db.Holdings.Remove(holding);
                }

                continue;
            }

            if (holding is null)
            {
                db.Holdings.Add(Holding.Create(tenantId, accountId, instrumentId, quantity, cost, currency));
                continue;
            }

            if (holding.ReplaceTotals(quantity, cost, currency))
            {
                db.Holdings.Remove(holding);
            }
        }
    }
}
