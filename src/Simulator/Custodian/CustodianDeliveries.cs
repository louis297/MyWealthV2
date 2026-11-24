using Microsoft.EntityFrameworkCore;
using MyWealthV2.Simulator.Broker;
using MyWealthV2.Simulator.Data;
using MyWealthV2.Simulator.Market;

namespace MyWealthV2.Simulator.Custodian;

/// <summary>In-kind-transfers R67: the custodian receives or delivers the shares of a settled fill.</summary>
public static class CustodianDeliveries
{
    public const string BrokerName = "Simulator Broker";

    /// <summary>One <c>Trade</c> movement when the order names a safekeeping account the custodian holds in the
    /// order's currency; nothing otherwise. The caller holds the broker's and the custodian's locks.</summary>
    public static async Task DeliverAsync(SimulatorDbContext db, BrokerOrder order, BrokerFill fill, DateOnly settledDate, CancellationToken cancellationToken)
    {
        if (order.SafekeepingAccount is null)
        {
            return;
        }

        var currency = order.Currency.Trim();
        var account = await db.CustodianAccounts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.AccountNumber == order.SafekeepingAccount && row.Currency == currency, cancellationToken);
        if (account is null)
        {
            return;
        }

        var world = await db.Worlds.AsNoTracking().SingleAsync(cancellationToken);
        var fillReport = await db.BrokerReports.AsNoTracking()
            .Where(report => report.Kind == BrokerReportKind.Fill && report.FillId == fill.Id)
            .Select(report => report.Id)
            .SingleAsync(cancellationToken);
        var number = await db.BrokerReports.CountAsync(report => report.Id <= fillReport, cancellationToken);
        await CustodianBook.AddAsync(db, account.AccountNumber,
            order.Side == BrokerOrderSide.Buy ? CustodianMovement.InDirection : CustodianMovement.OutDirection,
            CustodianMovementKind.Trade, order.Symbol, currency, fill.Quantity, settledDate, DateTimeOffset.UtcNow,
            tradeReference: BrokerBook.ReportId(world.WorldId, number), counterpartyName: BrokerName, cancellationToken: cancellationToken);
    }
}
