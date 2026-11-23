using MyWealthV2.Application.Broker;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Orders;
using MyWealthV2.Domain.Rebalances;

namespace MyWealthV2.Application.Rebalances;

/// <summary>
/// Rebalances R14–R16: the one place a rebalance places orders, for confirm and the advance step. The caller holds
/// the Account lock, has checked R6 and made the plan; this places the next wave from it and commits nothing.
/// </summary>
public sealed class PlaceRebalanceWave(IApplicationDbContext db, IBrokerConnections connections)
{
    /// <summary>The memo of every order a rebalance places (R15).</summary>
    public const string Memo = "Rebalance";

    /// <summary>
    /// R14: the first wave is every sell of the plan; a plan with no sell, and every later wave, is the plan's buys,
    /// sized by their reservation (R16). Sells are placed only in the first wave. Returns the orders placed; none when
    /// there is nothing to place, and then the rebalance is not moved.
    /// </summary>
    public async Task<IReadOnlyList<Order>> PlaceAsync(
        Rebalance rebalance,
        Account account,
        PlannedRebalance planned,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var sells = rebalance.Waves == 0 ? planned.Plan.Trades.Where(trade => !trade.Buy).ToList() : [];
        var trades = sells.Count > 0 ? sells : planned.Plan.Trades.Where(trade => trade.Buy).ToList();
        if (trades.Count == 0)
        {
            return [];
        }

        var side = sells.Count > 0 ? OrderSide.Sell : OrderSide.Buy;
        var connection = rebalance.Connection!;
        var placedBy = rebalance.ConfirmedByUserId!.Value;
        var priceDates = planned.Holdings.Select(row => (row.InstrumentId, row.PriceDate))
            .Concat(planned.Targets.Select(row => (row.InstrumentId, row.PriceDate)))
            .Where(row => row.PriceDate is not null)
            .GroupBy(row => row.InstrumentId)
            .ToDictionary(group => group.Key, group => group.First().PriceDate!.Value);

        var orders = new List<Order>();
        foreach (var trade in trades)
        {
            planned.FeeSchedules.TryGetValue(trade.QuoteCurrency, out var fee);
            var terms = fee is null ? OrderFeeTerms.None : new OrderFeeTerms(fee.Schedule, fee.DecimalPlaces);
            var order = Order.Place(
                account.TenantId, account.Id, trade.InstrumentId, side, trade.Quantity, trade.QuoteCurrency, trade.Price,
                priceDates[trade.InstrumentId], terms, connection, Memo, placedBy, now,
                settlementAccount: await SettlementAccountAsync(account, connection, trade.QuoteCurrency, cancellationToken),
                rebalanceId: rebalance.Id);
            // A wave the worker places has no person; the auditing interceptor keeps a person when there is one.
            order.CreatedBy = SystemActor.Name;
            orders.Add(order);
        }

        // R16 and the invariant of §5: a buy wave never reserves more than the available cash.
        var reserved = orders.Sum(order => order.ReservedCash);
        if (reserved > planned.Cash)
        {
            throw new RebalanceException(
                "insufficient_cash",
                $"The buys would reserve {reserved:0.00} {account.Currency} and the account's available cash is {planned.Cash:0.00} {account.Currency}.");
        }

        rebalance.StartWave(side);
        db.Orders.AddRange(orders);
        return orders;
    }

    /// <summary>Trade-settlement R3: as a person's order, the Tenant's active client money account in the Account's
    /// currency, when the Instrument is quoted in it and the connection holds the broker's bank account for it.</summary>
    private async Task<ClientMoneyAccount?> SettlementAccountAsync(
        Account account, string connection, string quoteCurrency, CancellationToken cancellationToken)
    {
        var currency = quoteCurrency.Trim().ToUpperInvariant();
        if (!string.Equals(currency, account.Currency.Trim(), StringComparison.OrdinalIgnoreCase)
            || connections.BankAccount(connection, currency) is null)
        {
            return null;
        }

        return await db.ClientMoneyAccounts.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.TenantId == account.TenantId && row.IsActive && row.Currency == currency)
            .SingleOrDefaultAsync(cancellationToken);
    }
}
