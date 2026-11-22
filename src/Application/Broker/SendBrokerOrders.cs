using Microsoft.Extensions.Logging;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Orders;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.Broker;

/// <summary>What a step of the broker sync did: how many orders it handled and how many calls failed or were left
/// for the next run.</summary>
public sealed record BrokerStepOutcome(int Handled, int Failed)
{
    public static BrokerStepOutcome None { get; } = new(0, 0);
}

/// <summary>
/// The send step and the cancel step of <c>broker-sync:{connection}</c> (docs/features/orders.md R20–R22; ADR 0021
/// rules 12, 13). Each attempt is recorded and committed before the call; each outcome is one database transaction
/// guarded by the order's row version. One order's failure never stops the others. Reads past the tenant query filter.
/// </summary>
public sealed class SendBrokerOrders(
    IApplicationDbContext db,
    IBrokerOrders broker,
    IBrokerConnections connections,
    ICurrencyCatalog currencies,
    TimeProvider time,
    ILogger<SendBrokerOrders> logger)
{
    /// <summary>Both steps, in order: the orders (R20), then the cancel requests (R22).</summary>
    public async Task<BrokerStepOutcome> RunAsync(string connection, CancellationToken cancellationToken = default)
    {
        var sent = await SendAsync(connection, cancellationToken);
        var cancelled = await CancelAsync(connection, cancellationToken);
        return new BrokerStepOutcome(sent.Handled + cancelled.Handled, sent.Failed + cancelled.Failed);
    }

    /// <summary>R20, R21: every <c>Placed</c> order of the connection with no cancel request, oldest first.</summary>
    public async Task<BrokerStepOutcome> SendAsync(string connection, CancellationToken cancellationToken = default)
    {
        if (!connections.IsConfigured(connection))
        {
            logger.LogWarning("Broker connection {Connection} is not configured on this host; nothing is sent.", connection);
            return BrokerStepOutcome.None;
        }

        db.ClearTrackedChanges();
        var ids = await (
                from order in db.Orders.IgnoreQueryFilters().AsNoTracking()
                join tenant in db.Tenants.AsNoTracking() on order.TenantId equals tenant.Id
                where order.Connection == connection
                      && order.Status == OrderStatus.Placed
                      && order.CancelRequestedAt == null
                      // R20: a disabled Tenant gets no first attempt; an attempted order is sent again.
                      && (tenant.IsActive || order.Attempts > 0)
                orderby order.Id
                select order.Id)
            .ToListAsync(cancellationToken);

        var failed = 0;
        foreach (var id in ids)
        {
            try
            {
                if (!await SendOneAsync(connection, id, cancellationToken))
                {
                    failed++;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failed++;
                logger.LogError(exception, "Order {OrderId} could not be sent on connection {Connection}; the next run tries again.", id, connection);
            }
        }

        return new BrokerStepOutcome(ids.Count, failed);
    }

    /// <summary>R22: every open order of the connection with a cancel request the broker has not answered, oldest
    /// first.</summary>
    public async Task<BrokerStepOutcome> CancelAsync(string connection, CancellationToken cancellationToken = default)
    {
        if (!connections.IsConfigured(connection))
        {
            logger.LogWarning("Broker connection {Connection} is not configured on this host; nothing is cancelled.", connection);
            return BrokerStepOutcome.None;
        }

        db.ClearTrackedChanges();
        var open = Enum.GetValues<OrderStatus>().Where(Order.IsOpenStatus).ToArray();
        var ids = await db.Orders.IgnoreQueryFilters().AsNoTracking()
            .Where(order => order.Connection == connection
                            && open.Contains(order.Status)
                            && order.CancelRequestedAt != null
                            && order.CancelAnsweredAt == null)
            .OrderBy(order => order.Id)
            .Select(order => order.Id)
            .ToListAsync(cancellationToken);

        var failed = 0;
        foreach (var id in ids)
        {
            try
            {
                if (!await CancelOneAsync(connection, id, cancellationToken))
                {
                    failed++;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failed++;
                logger.LogError(exception, "The cancel of order {OrderId} could not be sent on connection {Connection}; the next run tries again.", id, connection);
            }
        }

        return new BrokerStepOutcome(ids.Count, failed);
    }

    /// <summary>True when the broker answered; false when the call failed and the order waits for the next run.</summary>
    private async Task<bool> SendOneAsync(string connection, int id, CancellationToken cancellationToken)
    {
        db.ClearTrackedChanges();
        var order = await db.Orders.IgnoreQueryFilters().SingleAsync(row => row.Id == id, cancellationToken);
        if (order.Status != OrderStatus.Placed || order.HasCancelRequest)
        {
            return true;
        }

        // R20: the call is built from the stored order only, so every attempt sends the same body.
        var symbol = await db.Instruments.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.Id == order.InstrumentId)
            .Select(row => row.Symbol)
            .SingleAsync(cancellationToken);
        var request = new BrokerOrderRequest(
            ClientOrderId(order), symbol, order.Currency.Trim(), order.Side, order.Quantity, order.Type, order.TimeInForce,
            order.PriceLimit, order.ReferencePrice);

        order.RecordAttempt(time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);

        var result = await broker.PlaceAsync(connection, request, cancellationToken);

        try
        {
            Apply(order, result);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // R21: a report moved the order on while the call was out. It keeps its status and takes the broker's id.
            db.ClearTrackedChanges();
            order = await db.Orders.IgnoreQueryFilters().SingleAsync(row => row.Id == id, cancellationToken);
            Apply(order, result);
            await db.SaveChangesAsync(cancellationToken);
        }

        if (result.Failure is { } failure)
        {
            logger.LogError(
                "Order {Order} failed on connection {Connection}: {Failure}; it is sent again on the next run.",
                order.PublicId, connection, failure);
            return false;
        }

        return true;
    }

    /// <summary>R21: accepted, rejected, or the failure of the call.</summary>
    private void Apply(Order order, BrokerPlaceResult result)
    {
        if (result.BrokerOrderId is { } brokerOrderId)
        {
            order.Accept(brokerOrderId, time.GetUtcNow());
        }
        else if (result.RejectReason is { } reason)
        {
            // An order a report already moved on is not rejected: the report is the fact.
            if (order.Status == OrderStatus.Placed)
            {
                order.Reject(reason, time.GetUtcNow());
            }
        }
        else if (order.Status == OrderStatus.Placed)
        {
            order.RecordFailure(result.Failure!.Value);
        }
    }

    /// <summary>True when the broker answered; false when the call failed and the request waits for the next run.</summary>
    private async Task<bool> CancelOneAsync(string connection, int id, CancellationToken cancellationToken)
    {
        db.ClearTrackedChanges();
        var order = await db.Orders.IgnoreQueryFilters().Include(row => row.Fills).SingleAsync(row => row.Id == id, cancellationToken);
        if (!order.IsOpen || !order.HasCancelRequest || order.CancelAnsweredAt is not null)
        {
            return true;
        }

        var result = await broker.CancelAsync(connection, ClientOrderId(order), cancellationToken);
        if (result.Failure is { } failure)
        {
            logger.LogError(
                "The cancel of order {Order} failed on connection {Connection}: {Failure}; it is asked again on the next run.",
                order.PublicId, connection, failure);
            return false;
        }

        var now = time.GetUtcNow();
        if (result.Answer == BrokerCancelAnswer.UnknownOrder)
        {
            // R22: the broker never had the order, so only the platform can end it.
            order.EndAsUnknown(now, await OrderFees.OfAsync(db, currencies, order, cancellationToken));
        }
        else
        {
            // Accepted: the Cancelled report ends the order. TooLate: the reports say how it ended.
            order.AnswerCancel(now);
        }

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>R20: the order's PublicId is the client order id on every attempt.</summary>
    public static string ClientOrderId(Order order) => order.PublicId.ToString("D");
}
