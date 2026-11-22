using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Orders;

namespace MyWealthV2.Application.Orders;

/// <summary>Orders R11: the fee terms an order was placed with, read again for a move that changes what it reserves
/// or charges a fill.</summary>
internal static class OrderFees
{
    public static async Task<OrderFeeTerms> OfAsync(
        IApplicationDbContext db,
        ICurrencyCatalog currencies,
        Order order,
        CancellationToken cancellationToken)
    {
        var places = currencies.TryGet(order.Currency.Trim())?.DecimalPlaces ?? 2;
        if (order.TradeFeeScheduleId is not int scheduleId)
        {
            return new OrderFeeTerms(null, places);
        }

        var schedule = await db.TradeFeeSchedules.IgnoreQueryFilters()
            .Include(row => row.BandRows)
            .SingleAsync(row => row.Id == scheduleId, cancellationToken);
        return new OrderFeeTerms(schedule, places);
    }
}
