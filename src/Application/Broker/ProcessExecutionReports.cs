using Microsoft.Extensions.Logging;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Orders;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Orders;

namespace MyWealthV2.Application.Broker;

/// <summary>What a processing run did: reports processed, skipped, and failed or left <c>Received</c> for the next
/// run.</summary>
public sealed record ReportProcessingOutcome(int Processed, int Skipped, int Failed);

/// <summary>
/// Turns the stored execution reports of one broker connection into changes of their orders
/// (docs/features/orders.md R25–R29; ADR 0021 rule 8). Each report, oldest first, is processed in its own database
/// transaction that also sets its status. Reads past the tenant query filter and names each Tenant itself. The books
/// follow the broker: the state of the Tenant, the Customer or the order does not stop a report (R29).
/// </summary>
public sealed class ProcessExecutionReports(
    IApplicationDbContext db,
    IBrokerConnections connections,
    ICurrencyCatalog currencies,
    PostOrderFills poster,
    TimeProvider time,
    ILogger<ProcessExecutionReports> logger)
{
    public async Task<ReportProcessingOutcome> RunAsync(string connection, CancellationToken cancellationToken = default)
    {
        if (!connections.IsConfigured(connection))
        {
            logger.LogWarning("Broker connection {Connection} is not configured on this host; nothing is processed.", connection);
            return new ReportProcessingOutcome(0, 0, 0);
        }

        db.ClearTrackedChanges();
        var ids = await db.ExecutionReports.AsNoTracking()
            .Where(row => row.Connection == connection && row.Status == ExecutionReportStatus.Received)
            .OrderBy(row => row.ReceivedAt)
            .ThenBy(row => row.Id)
            .Select(row => row.Id)
            .ToListAsync(cancellationToken);

        var (processed, skipped, failed) = (0, 0, 0);
        foreach (var id in ids)
        {
            db.ClearTrackedChanges();
            try
            {
                var status = ExecutionReportStatus.Received;
                await db.ExecuteInTransactionAsync(async token =>
                {
                    status = ExecutionReportStatus.Received;
                    var report = await db.ExecutionReports.SingleAsync(row => row.Id == id, token);
                    if (report.Status != ExecutionReportStatus.Received)
                    {
                        return;
                    }

                    var result = await ProcessAsync(report, token);
                    if (result.Left)
                    {
                        return;
                    }

                    if (result.SkipReason is { } reason)
                    {
                        report.Skip(reason, time.GetUtcNow());
                    }
                    else
                    {
                        report.MarkProcessed(time.GetUtcNow());
                    }

                    await db.SaveChangesAsync(token);
                    status = report.Status;
                }, cancellationToken);
                if (status == ExecutionReportStatus.Processed)
                {
                    processed++;
                }
                else if (status == ExecutionReportStatus.Skipped)
                {
                    skipped++;
                }
                else
                {
                    failed++;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failed++;
                logger.LogError(exception, "Execution report {ReportId} could not be processed; the next run tries again.", id);
            }
        }

        return new ReportProcessingOutcome(processed, skipped, failed);
    }

    /// <summary>R25: applies the report to its order. Processed when it changed the order, else the first skip reason
    /// that applies.</summary>
    private async Task<ReportResult> ProcessAsync(ExecutionReport report, CancellationToken cancellationToken)
    {
        if (report.Kind is not { } kind)
        {
            return ReportResult.Skipped(ExecutionReportSkipReason.UnsupportedKind);
        }

        // Trade-settlement R7, R8: a Settlement report names no order.
        if (kind == ExecutionReportKind.Settlement)
        {
            return await SettleAsync(report, cancellationToken);
        }

        // R25: the client order id is the PublicId of an order that was placed on this connection.
        if (!Guid.TryParse(report.ClientOrderId, out var orderPublicId))
        {
            return ReportResult.Skipped(ExecutionReportSkipReason.UnknownOrder);
        }

        var located = await db.Orders.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.PublicId == orderPublicId && row.Connection == report.Connection)
            .Select(row => new { row.Id, row.AccountId })
            .SingleOrDefaultAsync(cancellationToken);
        if (located is null)
        {
            return ReportResult.Skipped(ExecutionReportSkipReason.UnknownOrder);
        }

        // The order changes what its Account reserves, so it is read under the Account lock (R31, posting R22).
        await db.LockAccountAsync(located.AccountId, cancellationToken);
        var order = await db.Orders.IgnoreQueryFilters().Include(row => row.Fills)
            .SingleAsync(row => row.Id == located.Id, cancellationToken);
        var fees = await OrderFees.OfAsync(db, currencies, order, cancellationToken);
        var now = time.GetUtcNow();

        switch (kind)
        {
            case ExecutionReportKind.Fill:
                // R25: a fill is within the order's quantity minus its filled quantity, whatever the order's status.
                if (report.Quantity!.Value > order.Quantity - order.FilledQuantity)
                {
                    return ReportResult.Skipped(ExecutionReportSkipReason.Overfill);
                }

                var fill = order.AddFill(
                    report.Id, report.Quantity.Value, report.Price!.Value, report.TradeDate!.Value, report.SettlementDate!.Value,
                    report.ContractNote, now, fees);

                // R26, R31: in the same database transaction the platform tries to post it. A fill that cannot be
                // posted waits; its report is still processed (R36).
                await poster.PostAsync(order, fill, fees, cancellationToken);
                return ReportResult.Processed;

            case ExecutionReportKind.Cancelled:
            case ExecutionReportKind.Expired:
                // R27: the report ends an open order; one for an order that is not open changes nothing.
                if (!order.IsOpen)
                {
                    return ReportResult.Skipped(ExecutionReportSkipReason.Repeated);
                }

                order.End(
                    kind == ExecutionReportKind.Cancelled ? OrderStatus.Cancelled : OrderStatus.Expired, report.Reason, now, fees);
                return ReportResult.Processed;

            case ExecutionReportKind.Broken:
                // Trade-settlement R27: a fill is broken once, and never after it settled.
                var breaking = await FillNamedAsync(order, report, cancellationToken);
                if (breaking is null)
                {
                    return ReportResult.Skipped(ExecutionReportSkipReason.UnknownFill);
                }

                if (breaking.IsBroken)
                {
                    return ReportResult.Skipped(ExecutionReportSkipReason.Repeated);
                }

                if (breaking.IsSettled)
                {
                    return ReportResult.Skipped(ExecutionReportSkipReason.FillSettled);
                }

                await poster.BreakAsync(order, breaking, report, fees, cancellationToken);
                return ReportResult.Processed;

            default:
                // R43: the fill is named by the outside id of its Fill report. Settlement writes nothing to the ledger.
                var settling = await FillNamedAsync(order, report, cancellationToken);
                if (settling is null)
                {
                    return ReportResult.Skipped(ExecutionReportSkipReason.UnknownFill);
                }

                if (settling.IsBroken)
                {
                    return ReportResult.Skipped(ExecutionReportSkipReason.FillBroken);
                }

                if (settling.IsSettled)
                {
                    return ReportResult.Skipped(ExecutionReportSkipReason.Repeated);
                }

                settling.Settle(report.SettledDate!.Value, now, report.SettlementId);
                return ReportResult.Processed;
        }
    }

    /// <summary>What one report came to: processed, skipped for a reason, or left <c>Received</c>.</summary>
    /// <summary>R43, trade-settlement R26: the fill of the order named by the outside id of its Fill report.</summary>
    private async Task<OrderFill?> FillNamedAsync(Order order, ExecutionReport report, CancellationToken cancellationToken)
    {
        var fillReportId = await db.ExecutionReports.AsNoTracking()
            .Where(row => row.Connection == report.Connection && row.OutsideId == report.FillOutsideId)
            .Select(row => (long?)row.Id)
            .SingleOrDefaultAsync(cancellationToken);
        return order.Fills.SingleOrDefault(row => row.ExecutionReportId == fillReportId);
    }

    /// <summary>
    /// Trade-settlement R8, R9, R11–R13: the fills of the connection that carry the broker settlement id and are in no
    /// trade settlement yet, grouped by the client money account of their order, each group stored as one trade
    /// settlement with the platform's own figures.
    /// </summary>
    private async Task<ReportResult> SettleAsync(ExecutionReport report, CancellationToken cancellationToken)
    {
        var settlementId = report.SettlementId!;
        if (await db.TradeSettlements.IgnoreQueryFilters()
                .AnyAsync(row => row.Connection == report.Connection && row.BrokerSettlementId == settlementId, cancellationToken))
        {
            return ReportResult.Skipped(ExecutionReportSkipReason.Repeated);
        }

        var fills = await (
                from fill in db.OrderFills.IgnoreQueryFilters()
                join order in db.Orders.IgnoreQueryFilters() on fill.OrderId equals order.Id
                where order.Connection == report.Connection
                      && fill.BrokerSettlementId == settlementId
                      && fill.TradeSettlementId == null
                      && fill.Status != OrderFillStatus.Broken
                      && order.ClientMoneyAccountId != null
                select new { fill.Id, ClientMoneyAccountId = order.ClientMoneyAccountId!.Value, order.Side, fill.Quantity, fill.Price })
            .ToListAsync(cancellationToken);
        if (fills.Count == 0)
        {
            return ReportResult.Skipped(ExecutionReportSkipReason.UnknownSettlement);
        }

        var now = time.GetUtcNow();
        foreach (var group in fills.GroupBy(fill => fill.ClientMoneyAccountId).OrderBy(group => group.Key))
        {
            var clientMoney = await db.ClientMoneyAccounts.IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(row => row.Id == group.Key, cancellationToken);
            var settlement = TradeSettlement.Create(
                clientMoney, report.Connection, settlementId, report.SettledDate!.Value, report.Id,
                group.Select(fill => new SettlementFill(fill.Side, fill.Quantity, fill.Price)).ToList(),
                report.FillCount!.Value, report.NetAmount!.Value, report.SettlementAccount!, report.Currency!,
                connections.BankAccount(report.Connection, clientMoney.Currency.Trim()) is not null, now);
            settlement.CreatedBy = SystemActor.Name;
            db.TradeSettlements.Add(settlement);
            await db.SaveChangesAsync(cancellationToken);

            // Trade-settlement R12: the firm's net is paid by the worker's send step, with no approval.
            if (settlement.Status == TradeSettlementStatus.AwaitingPayment)
            {
                var payment = OutgoingPayment.ForTradeSettlement(settlement, clientMoney);
                payment.CreatedBy = SystemActor.Name;
                db.OutgoingPayments.Add(payment);
            }

            var ids = group.Select(fill => fill.Id).ToList();
            foreach (var fill in await db.OrderFills.IgnoreQueryFilters().Where(row => ids.Contains(row.Id)).ToListAsync(cancellationToken))
            {
                fill.AssignTo(settlement.Id);
            }
        }

        return ReportResult.Processed;
    }

    private readonly record struct ReportResult(ExecutionReportSkipReason? SkipReason, bool Left)
    {
        public static ReportResult Processed => new(null, false);

        public static ReportResult Waiting => new(null, true);

        public static ReportResult Skipped(ExecutionReportSkipReason reason) => new(reason, false);
    }
}
