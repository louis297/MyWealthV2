using Microsoft.EntityFrameworkCore;
using MyWealthV2.Simulator.Data;
using MyWealthV2.Simulator.Market;
using MyWealthV2.Simulator.Worlds;

namespace MyWealthV2.Simulator.Broker;

/// <summary>The simulator broker's books (orders R52): the steps every broker route shares.</summary>
public static class BrokerBook
{
    /// <summary>R52: before a broker route answers, the market is brought up to date (simulated-instruments R18).
    /// Answers the world.</summary>
    public static Task<World> UpToDateAsync(SimulatorDbContext db, TimeProvider time, CancellationToken cancellationToken) =>
        MarketControlEndpoints.UpToDateAsync(db, time, cancellationToken);

    /// <summary>Takes the lock every write on the broker's books holds until its transaction ends, so the numbers of
    /// orders and fills and the places of reports follow the order the writes happen in.</summary>
    public static Task LockAsync(SimulatorDbContext db, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlRawAsync(
            "SELECT 1 FROM [BrokerSettings] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = 1", cancellationToken);

    /// <summary>
    /// R56, R57: fills <paramref name="quantity"/> of a working order and appends its <c>Fill</c> report. The price
    /// follows the market's price of the day for a simulated instrument and the order's reference price for any
    /// other symbol, moved by the slippage. A price beyond the order's limit fills nothing: null. The caller holds
    /// the lock and commits.
    /// </summary>
    public static async Task<BrokerFill?> FillAsync(
        SimulatorDbContext db,
        World world,
        BrokerSettings settings,
        BrokerOrder order,
        decimal quantity,
        DateOnly day,
        CancellationToken cancellationToken)
    {
        var number = (await db.BrokerFills.MaxAsync(fill => (int?)fill.Number, cancellationToken) ?? 0) + 1;
        var price = BrokerRules.FillPrice(
            await BasePriceAsync(db, order, cancellationToken), settings.SlippagePercent, world.Seed, number);
        if (!order.AllowsPrice(price))
        {
            return null;
        }

        var fill = order.Fill(number, quantity, price, day, BrokerRules.SettlementDate(day, settings.SettlementDays));
        db.BrokerReports.Add(BrokerReport.OfFill(order, fill));
        await db.SaveChangesAsync(cancellationToken);
        return fill;
    }

    /// <summary>R56: the market's price of the current simulator day for a simulated instrument; the order's
    /// reference price for any other symbol.</summary>
    private static async Task<decimal> BasePriceAsync(SimulatorDbContext db, BrokerOrder order, CancellationToken cancellationToken)
    {
        var instrument = await db.MarketInstruments.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Symbol == order.Symbol, cancellationToken);
        return instrument?.Price ?? order.ReferencePrice;
    }
}
