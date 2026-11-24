using Microsoft.EntityFrameworkCore;
using MyWealthV2.Simulator.Data;
using MyWealthV2.Simulator.Market;
using MyWealthV2.Simulator.Worlds;

namespace MyWealthV2.Simulator.Broker;

/// <summary>The simulator broker's books (orders R52): the steps every broker route shares.</summary>
public static class BrokerBook
{
    /// <summary>R52: before a broker route answers, the market is brought up to date (simulated-instruments R18) and
    /// then the broker: R62, R60, R61. Answers the world and leaves the change tracker empty.</summary>
    public static async Task<World> UpToDateAsync(
        SimulatorDbContext db,
        TimeProvider time,
        BrokerBankAccounts accounts,
        CancellationToken cancellationToken)
    {
        var world = await MarketControlEndpoints.UpToDateAsync(db, time, cancellationToken);
        await BringUpToDateAsync(db, time, accounts, MarketPricePath.Day(world.Now(time.GetUtcNow())), cancellationToken);
        return await db.Worlds.AsNoTracking().SingleAsync(cancellationToken);
    }

    /// <summary>Takes the lock every write on the broker's books holds until its transaction ends, so the numbers of
    /// orders and fills and the places of reports follow the order the writes happen in.</summary>
    public static Task LockAsync(SimulatorDbContext db, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlRawAsync(
            "SELECT 1 FROM [BrokerSettings] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = 1", cancellationToken);

    /// <summary>
    /// What the clock did to the broker's books up to <paramref name="today"/>, in this order: a corporate action
    /// that became effective cancels the working orders in its symbol (R62); an order still working from an earlier
    /// simulator day expires (R60); a fill whose settlement date has come settles (R61). Each appends one report.
    /// </summary>
    private static async Task BringUpToDateAsync(
        SimulatorDbContext db,
        TimeProvider time,
        BrokerBankAccounts accounts,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var due = await db.BrokerOrders.AsNoTracking()
                      .AnyAsync(order => order.Status == BrokerOrderStatus.Working && order.AcceptedDay < today, cancellationToken)
                  || await db.BrokerFills.AsNoTracking()
                      .AnyAsync(fill => !fill.Settled && fill.SettlementDate <= today, cancellationToken)
                  || await db.MarketCorporateActions.AsNoTracking()
                      .AnyAsync(action => action.Status == MarketCorporateActionStatus.Effective && !action.CustodianApplied, cancellationToken);
        if (!due)
        {
            return;
        }

        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await LockAsync(db, cancellationToken);
            await Custodian.CustodianBook.LockAsync(db, cancellationToken);

            // In-kind-transfers R68: an action that became effective first settles every open fill in its symbol on the
            // effective day, then changes the custodian's positions in it.
            var effective = await (
                    from action in db.MarketCorporateActions
                    join instrument in db.MarketInstruments on action.InstrumentId equals instrument.Id
                    where action.Status == MarketCorporateActionStatus.Effective && !action.CustodianApplied
                    orderby action.ExDate, action.Number
                    select new { action, instrument.Symbol })
                .ToListAsync(cancellationToken);
            foreach (var item in effective)
            {
                var open = await (
                        from fill in db.BrokerFills
                        join order in db.BrokerOrders on fill.OrderId equals order.Id
                        where !fill.Settled && !fill.Broken && order.Symbol == item.Symbol
                        orderby fill.Number
                        select new SettlingFill(fill, order))
                    .ToListAsync(cancellationToken);
                await SettleDayAsync(db, time, accounts, item.action.ExDate, open, cancellationToken);
                // Dividends R56: a dividend changes no position; the custodian stores what it owes each account.
                if (item.action.Kind == MarketCorporateActionKind.Dividend)
                {
                    await Custodian.CustodianDividends.GoExAsync(db, item.action, item.Symbol, cancellationToken);
                }
                else
                {
                    await Custodian.CustodianCorporateActions.ApplyAsync(db, item.action, item.Symbol, cancellationToken);
                }

                item.action.MarkCustodianApplied();
                await db.SaveChangesAsync(cancellationToken);
            }

            var stale = await db.BrokerOrders
                .Where(order => order.Status == BrokerOrderStatus.Working && order.AcceptedDay < today)
                .OrderBy(order => order.Number)
                .ToListAsync(cancellationToken);

            // R62: an order taken before the ex-date of an action that is now effective is on the old share basis.
            foreach (var order in stale)
            {
                var purged = await (
                        from action in db.MarketCorporateActions.AsNoTracking()
                        join instrument in db.MarketInstruments.AsNoTracking() on action.InstrumentId equals instrument.Id
                        where instrument.Symbol == order.Symbol
                              && action.Status == MarketCorporateActionStatus.Effective
                              && action.ExDate > order.AcceptedDay
                              && action.ExDate <= today
                        select action.Id)
                    .AnyAsync(cancellationToken);
                if (purged)
                {
                    await CancelAsync(db, order, BrokerRules.CorporateActionReason, cancellationToken);
                }
            }

            // R60: a day order ends with its day.
            foreach (var order in stale.Where(order => order.Status == BrokerOrderStatus.Working))
            {
                order.Expire();
                db.BrokerReports.Add(BrokerReport.OfExpiry(order));
                await db.SaveChangesAsync(cancellationToken);
            }

            // R61: settlement, in the order the fills were made, date by date. Trade-settlement R36: the fills of
            // orders with a settlement account are netted per account and currency into one broker settlement, whose
            // Settlement report follows their Settled reports. A broken fill never settles (R40).
            var settling = await (
                    from fill in db.BrokerFills
                    join order in db.BrokerOrders on fill.OrderId equals order.Id
                    where !fill.Settled && !fill.Broken && fill.SettlementDate <= today
                    orderby fill.SettlementDate, fill.Number
                    select new SettlingFill(fill, order))
                .ToListAsync(cancellationToken);
            foreach (var day in settling.GroupBy(item => item.Fill.SettlementDate))
            {
                await SettleDayAsync(db, time, accounts, day.Key, day.ToList(), cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        });
        db.ChangeTracker.Clear();
    }

    private sealed record SettlingFill(BrokerFill Fill, BrokerOrder Order);

    /// <summary>
    /// R61: the fills settle on <paramref name="day"/>. Trade-settlement R36: the fills of orders with a settlement
    /// account are netted per account and currency into one broker settlement, whose Settlement report follows their
    /// Settled reports. In-kind-transfers R67: each settled fill of an order with a safekeeping account is delivered
    /// through the custodian.
    /// </summary>
    private static async Task SettleDayAsync(
        SimulatorDbContext db, TimeProvider time, BrokerBankAccounts accounts, DateOnly day, IReadOnlyList<SettlingFill> fills,
        CancellationToken cancellationToken)
    {
        foreach (var item in fills.Where(item => item.Order.SettlementAccount is null))
        {
            item.Fill.Settle();
            db.BrokerReports.Add(BrokerReport.OfSettlement(item.Order, item.Fill, settledDate: day));
            await db.SaveChangesAsync(cancellationToken);
            await Custodian.CustodianDeliveries.DeliverAsync(db, item.Order, item.Fill, day, cancellationToken);
        }

        foreach (var batch in fills.Where(item => item.Order.SettlementAccount is not null)
                     .GroupBy(item => (Account: item.Order.SettlementAccount!.ToUpperInvariant(), Currency: item.Order.Currency.Trim())))
        {
            var number = (await db.BrokerSettlements.MaxAsync(row => (int?)row.Number, cancellationToken) ?? 0) + 1;
            var settlement = BrokerSettlement.Open(number, batch.First().Order.SettlementAccount!, batch.Key.Currency, day);
            foreach (var item in batch)
            {
                settlement.Add(item.Order, item.Fill);
            }

            db.BrokerSettlements.Add(settlement);
            await db.SaveChangesAsync(cancellationToken);
            foreach (var item in batch)
            {
                item.Fill.Settle(settlement.Id);
                db.BrokerReports.Add(BrokerReport.OfSettlement(item.Order, item.Fill, settlement, day));
                await db.SaveChangesAsync(cancellationToken);
                await Custodian.CustodianDeliveries.DeliverAsync(db, item.Order, item.Fill, day, cancellationToken);
            }

            db.BrokerReports.Add(BrokerReport.OfSettlementClosed(settlement));
            await db.SaveChangesAsync(cancellationToken);
            await AfterSettlementAsync(db, time, accounts, settlement, cancellationToken);
        }
    }

    /// <summary>Trade-settlement R37, R38: in mode <c>Pay</c> the broker pays a net above 0 at once.</summary>
    private static async Task AfterSettlementAsync(
        SimulatorDbContext db,
        TimeProvider time,
        BrokerBankAccounts accounts,
        BrokerSettlement settlement,
        CancellationToken cancellationToken)
    {
        var settings = await db.BrokerSettings.AsNoTracking().SingleAsync(cancellationToken);
        if (settlement.NetAmount > 0m && settings.SettlementMode == BrokerSettlementMode.Pay)
        {
            await PayAsync(db, time, accounts, settlement, settlement.NetAmount, cancellationToken);
        }
    }

    /// <summary>
    /// Trade-settlement R37, R38: the broker pays <paramref name="amount"/> of a settlement as a credit line on the
    /// trust account at the simulator bank, with the settlement id as reference, entering the feed at once. False when
    /// the settlement account is not an open account of the bank in that currency: no line is added. The caller
    /// holds the lock and commits.
    /// </summary>
    public static async Task<bool> PayAsync(
        SimulatorDbContext db,
        TimeProvider time,
        BrokerBankAccounts accounts,
        BrokerSettlement settlement,
        decimal amount,
        CancellationToken cancellationToken)
    {
        var currency = settlement.Currency.Trim();
        var trust = await db.BankAccounts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.AccountNumber == settlement.SettlementAccount && row.Currency == currency, cancellationToken);
        if (trust is null)
        {
            return false;
        }

        var world = await db.Worlds.SingleAsync(cancellationToken);
        var simulatorNow = world.Now(time.GetUtcNow());
        var payer = accounts.In(currency);
        db.BankLines.Add(Bank.BankLine.Receipt(
            world.TakeLineOutsideId(),
            trust.Id,
            amount,
            DateOnly.FromDateTime(simulatorNow.UtcDateTime),
            payer?.Name ?? BrokerBankAccounts.DefaultPayerName,
            payer?.AccountNumber,
            BrokerSettlement.SettlementId(world.WorldId, settlement.Number),
            simulatorNow,
            simulatorNow,
            deliverTwice: false));
        settlement.RecordPaid(amount);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// R56–R58: fills <paramref name="quantity"/> of a working order and appends its <c>Fill</c> report. Without a
    /// typed price the price follows the market's price of the day for a simulated instrument and the order's
    /// reference price for any other symbol, moved by the slippage. A price beyond the order's limit fills nothing:
    /// null. The caller holds the lock and commits.
    /// </summary>
    public static async Task<BrokerFill?> FillAsync(
        SimulatorDbContext db,
        World world,
        BrokerSettings settings,
        BrokerOrder order,
        decimal quantity,
        DateOnly day,
        CancellationToken cancellationToken,
        decimal? typedPrice = null)
    {
        var number = (await db.BrokerFills.MaxAsync(fill => (int?)fill.Number, cancellationToken) ?? 0) + 1;
        var price = typedPrice ?? BrokerRules.FillPrice(
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

    /// <summary>R58, R59, R62: cancels a working order and appends its <c>Cancelled</c> report. The caller holds the
    /// lock and commits.</summary>
    public static async Task CancelAsync(SimulatorDbContext db, BrokerOrder order, string reason, CancellationToken cancellationToken)
    {
        order.Cancel();
        db.BrokerReports.Add(BrokerReport.OfCancel(order, reason));
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>R63: the outside id of a report, <c>{WorldId:N}-X{n}</c>, <c>n</c> counting the reports of the world
    /// from 1.</summary>
    public static string ReportId(Guid worldId, int number) =>
        $"{worldId:N}-X{number.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    /// <summary>R63: the number of a report outside id of <paramref name="worldId"/>, or false.</summary>
    public static bool TryReadReportNumber(string id, Guid worldId, out int number)
    {
        number = 0;
        var prefix = $"{worldId:N}-X";
        return id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
               && int.TryParse(id[prefix.Length..], System.Globalization.NumberStyles.None,
                   System.Globalization.CultureInfo.InvariantCulture, out number);
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
