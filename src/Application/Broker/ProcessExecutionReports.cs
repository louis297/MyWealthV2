using Microsoft.Extensions.Logging;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Orders;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;

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

            default:
                // Settlement (R43) is not processed yet: the report waits as Received.
                return ReportResult.Waiting;
        }
    }

    /// <summary>What one report came to: processed, skipped for a reason, or left <c>Received</c>.</summary>
    private readonly record struct ReportResult(ExecutionReportSkipReason? SkipReason, bool Left)
    {
        public static ReportResult Processed => new(null, false);

        public static ReportResult Waiting => new(null, true);

        public static ReportResult Skipped(ExecutionReportSkipReason reason) => new(reason, false);
    }
}
