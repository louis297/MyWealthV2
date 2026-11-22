using Microsoft.EntityFrameworkCore;
using MyWealthV2.Simulator.Control;
using MyWealthV2.Simulator.Data;
using MyWealthV2.Simulator.Market;
using MyWealthV2.Simulator.Worlds;

namespace MyWealthV2.Simulator.Broker;

/// <summary>Orders R55, R56, R58, R61, R64: the operator's hand on the simulator broker, and its own books. Every
/// write is logged (simulator R28).</summary>
public static class BrokerControlEndpoints
{
    public static RouteGroupBuilder MapBrokerControlEndpoints(this RouteGroupBuilder control)
    {
        var broker = control.MapGroup("/broker");
        broker.MapGet("/", GetSettingsAsync);
        broker.MapPost("/order-mode", SetOrderModeAsync);
        broker.MapPost("/slippage", SetSlippageAsync);
        broker.MapPost("/settlement-days", SetSettlementDaysAsync);
        broker.MapGet("/orders", GetOrdersAsync);
        broker.MapGet("/orders/{brokerOrderId}", GetOrderAsync);
        broker.MapPost("/orders/{brokerOrderId}/fill", FillOrderAsync);
        broker.MapPost("/orders/{brokerOrderId}/cancel", CancelOrderAsync);
        return control;
    }

    /// <summary>R64: the order mode, the slippage and the settlement days.</summary>
    private static async Task<IResult> GetSettingsAsync(SimulatorDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        await BrokerBook.UpToDateAsync(db, time, cancellationToken);
        return Results.Ok(BrokerSettingsItem.From(await db.BrokerSettings.AsNoTracking().SingleAsync(cancellationToken)));
    }

    /// <summary>R55: how the broker answers new orders.</summary>
    private static async Task<IResult> SetOrderModeAsync(
        BrokerOrderModeRequest body,
        HttpContext http,
        SimulatorDbContext db,
        TimeProvider time)
    {
        if (!TryName<BrokerOrderMode>(body.Mode, out var mode))
        {
            return ControlEndpoints.Refused("Mode", "mode must be Fill, Hold or Reject.");
        }

        if (body.FillPercent is not null && !BrokerRules.IsFillPercent(body.FillPercent))
        {
            return ControlEndpoints.Refused("FillPercent", "fillPercent must be a whole number from 1 to 100.");
        }

        BrokerRejectReason? reason = null;
        if (mode == BrokerOrderMode.Reject)
        {
            if (!TryName<BrokerRejectReason>(body.Reason, out var named))
            {
                return ControlEndpoints.Refused(
                    "Reason", $"reason must be one of {string.Join(", ", Enum.GetNames<BrokerRejectReason>())} with Reject.");
            }

            reason = named;
        }

        return await WriteAsync(http, db, time, ControlActions.BrokerOrderModeChanged, body,
            settings => settings.SetMode(mode, body.FillPercent ?? BrokerRules.DefaultFillPercent, reason));
    }

    /// <summary>R56: the slippage of later fills.</summary>
    private static async Task<IResult> SetSlippageAsync(
        BrokerSlippageRequest body,
        HttpContext http,
        SimulatorDbContext db,
        TimeProvider time)
    {
        if (!BrokerRules.IsSlippage(body.SlippagePercent))
        {
            return ControlEndpoints.Refused("SlippagePercent", "slippagePercent must be 0 to 5 with at most 2 decimal places.");
        }

        return await WriteAsync(http, db, time, ControlActions.BrokerSlippageSet, body,
            settings => settings.SetSlippage(body.SlippagePercent!.Value));
    }

    /// <summary>R61: the weekdays from trade to settlement of later fills.</summary>
    private static async Task<IResult> SetSettlementDaysAsync(
        BrokerSettlementDaysRequest body,
        HttpContext http,
        SimulatorDbContext db,
        TimeProvider time)
    {
        if (!BrokerRules.IsSettlementDays(body.SettlementDays))
        {
            return ControlEndpoints.Refused("SettlementDays", "settlementDays must be a whole number from 0 to 5.");
        }

        return await WriteAsync(http, db, time, ControlActions.BrokerSettlementDaysSet, body,
            settings => settings.SetSettlementDays(body.SettlementDays!.Value));
    }

    /// <summary>R64: the orders, newest first, with optional <c>status</c> and <c>symbol</c>.</summary>
    private static async Task<IResult> GetOrdersAsync(
        SimulatorDbContext db,
        TimeProvider time,
        string? status,
        string? symbol,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var world = await BrokerBook.UpToDateAsync(db, time, cancellationToken);
        var orders = db.BrokerOrders.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!TryName<BrokerOrderStatus>(status, out var named))
            {
                return ControlEndpoints.Refused(
                    "Status", $"status must be one of {string.Join(", ", Enum.GetNames<BrokerOrderStatus>())}.");
            }

            orders = orders.Where(order => order.Status == named);
        }

        if (!string.IsNullOrWhiteSpace(symbol))
        {
            var wanted = symbol.Trim();
            orders = orders.Where(order => order.Symbol == wanted);
        }

        var size = Math.Clamp(pageSize ?? ControlEndpoints.DefaultPageSize, 1, ControlEndpoints.MaxPageSize);
        var number = Math.Max(page ?? 1, 1);
        var total = await orders.CountAsync(cancellationToken);
        var rows = await orders
            .OrderByDescending(order => order.Number)
            .Skip((number - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);
        return Results.Ok(new BrokerOrderPage(
            rows.Select(order => BrokerOrderItem.From(order, world.WorldId)).ToList(), number, size, total));
    }

    /// <summary>R64: one order with its fills and its reports.</summary>
    private static async Task<IResult> GetOrderAsync(
        string brokerOrderId,
        SimulatorDbContext db,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var world = await BrokerBook.UpToDateAsync(db, time, cancellationToken);
        var order = await FindAsync(db, world, brokerOrderId, tracked: false, cancellationToken);
        return order is null ? Results.NotFound() : Results.Ok(await DetailAsync(db, world, order, cancellationToken));
    }

    /// <summary>R58: the operator fills a working order, wholly or in part, at the broker's price or a typed one.</summary>
    private static async Task<IResult> FillOrderAsync(
        string brokerOrderId,
        BrokerFillRequest body,
        HttpContext http,
        SimulatorDbContext db,
        TimeProvider time)
    {
        var cancellationToken = http.RequestAborted;
        var world = await BrokerBook.UpToDateAsync(db, time, cancellationToken);
        var realNow = time.GetUtcNow();
        var day = MarketPricePath.Day(world.Now(realNow));
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await BrokerBook.LockAsync(db, cancellationToken);
            var order = await FindAsync(db, world, brokerOrderId, tracked: true, cancellationToken);
            if (order is null)
            {
                return Results.NotFound();
            }

            if (order.Status != BrokerOrderStatus.Working)
            {
                return ControlEndpoints.Refused("Status", $"Only a working order can be filled; this one is {order.Status}.");
            }

            var quantity = body.Quantity ?? order.Rest;
            if (!BrokerRules.IsNumber(quantity) || quantity > order.Rest)
            {
                return ControlEndpoints.Refused(
                    "Quantity", "quantity must be greater than 0 with at most 8 decimal places and not more than the rest of the order.");
            }

            if (body.Price is not null && (!BrokerRules.IsPrice(body.Price) || !order.AllowsPrice(body.Price.Value)))
            {
                return ControlEndpoints.Refused(
                    "Price", "price must be greater than 0 with at most 4 decimal places and within the order's limit.");
            }

            var settings = await db.BrokerSettings.AsNoTracking().SingleAsync(cancellationToken);
            var fill = await BrokerBook.FillAsync(db, world, settings, order, quantity, day, cancellationToken, body.Price);
            if (fill is null)
            {
                return ControlEndpoints.Refused("Price", "The broker's price is beyond the order's limit. Give a price within the limit.");
            }

            MarketControlEndpoints.Log(
                db, http, world, realNow, ControlActions.BrokerOrderFilled, new { brokerOrderId, body.Quantity, body.Price });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(await DetailAsync(db, world, order, cancellationToken));
        });
    }

    /// <summary>R58: the operator cancels a working order as the broker.</summary>
    private static async Task<IResult> CancelOrderAsync(
        string brokerOrderId,
        BrokerCancelRequest body,
        HttpContext http,
        SimulatorDbContext db,
        TimeProvider time)
    {
        var cancellationToken = http.RequestAborted;
        var reason = body.Reason ?? BrokerRules.BrokerReason;
        if (reason.Length is < 1 or > BrokerRules.ReasonMaxLength)
        {
            return ControlEndpoints.Refused("Reason", "reason must be 1 to 140 characters.");
        }

        var world = await BrokerBook.UpToDateAsync(db, time, cancellationToken);
        var realNow = time.GetUtcNow();
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await BrokerBook.LockAsync(db, cancellationToken);
            var order = await FindAsync(db, world, brokerOrderId, tracked: true, cancellationToken);
            if (order is null)
            {
                return Results.NotFound();
            }

            if (order.Status != BrokerOrderStatus.Working)
            {
                return ControlEndpoints.Refused("Status", $"Only a working order can be cancelled; this one is {order.Status}.");
            }

            await BrokerBook.CancelAsync(db, order, reason, cancellationToken);
            MarketControlEndpoints.Log(
                db, http, world, realNow, ControlActions.BrokerOrderCancelled, new { brokerOrderId, body.Reason });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(await DetailAsync(db, world, order, cancellationToken));
        });
    }

    private static async Task<BrokerOrder?> FindAsync(
        SimulatorDbContext db,
        World world,
        string brokerOrderId,
        bool tracked,
        CancellationToken cancellationToken)
    {
        if (!BrokerOrder.TryReadNumber(brokerOrderId, world.WorldId, out var number))
        {
            return null;
        }

        var orders = tracked ? db.BrokerOrders : db.BrokerOrders.AsNoTracking();
        return await orders.SingleOrDefaultAsync(order => order.Number == number, cancellationToken);
    }

    private static async Task<BrokerOrderDetail> DetailAsync(
        SimulatorDbContext db,
        World world,
        BrokerOrder order,
        CancellationToken cancellationToken)
    {
        var fills = await db.BrokerFills.AsNoTracking()
            .Where(fill => fill.OrderId == order.Id)
            .OrderBy(fill => fill.Number)
            .ToListAsync(cancellationToken);
        var reports = await db.BrokerReports.AsNoTracking()
            .Where(report => report.OrderId == order.Id)
            .OrderBy(report => report.Id)
            .Select(report => new
            {
                report.Kind,
                report.SettledDate,
                report.Reason,
                Number = db.BrokerReports.Count(other => other.Id <= report.Id)
            })
            .ToListAsync(cancellationToken);
        return new BrokerOrderDetail(
            BrokerOrderItem.From(order, world.WorldId),
            fills.Select(fill => new BrokerFillItem(
                fill.Quantity, fill.Price, fill.TradeDate, fill.SettlementDate, fill.ContractNote, fill.Settled)).ToList(),
            reports.Select(report => new BrokerReportItem(
                BrokerBook.ReportId(world.WorldId, report.Number), report.Kind.ToString(), report.SettledDate, report.Reason)).ToList());
    }

    /// <summary>One write on the settings row: brings the books up to date (R52), applies it, appends the log row and
    /// saves both. A concurrent write on the row returns 409.</summary>
    private static async Task<IResult> WriteAsync(
        HttpContext http,
        SimulatorDbContext db,
        TimeProvider time,
        string action,
        object body,
        Action<BrokerSettings> apply)
    {
        var world = await BrokerBook.UpToDateAsync(db, time, http.RequestAborted);
        var settings = await db.BrokerSettings.SingleAsync(http.RequestAborted);
        apply(settings);
        MarketControlEndpoints.Log(db, http, world, time.GetUtcNow(), action, body);
        try
        {
            await db.SaveChangesAsync(http.RequestAborted);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "The broker settings were changed by another write. Read them again and retry.");
        }

        return Results.Ok(BrokerSettingsItem.From(settings));
    }

    /// <summary>An exact name of <typeparamref name="T"/>; a number is not a name.</summary>
    private static bool TryName<T>(string? value, out T named)
        where T : struct, Enum =>
        Enum.TryParse(value, ignoreCase: false, out named) && Enum.GetNames<T>().Contains(value, StringComparer.Ordinal);
}

public sealed record BrokerOrderModeRequest(string? Mode, int? FillPercent, string? Reason);

public sealed record BrokerSlippageRequest(decimal? SlippagePercent);

public sealed record BrokerSettlementDaysRequest(int? SettlementDays);

public sealed record BrokerFillRequest(decimal? Quantity, decimal? Price);

public sealed record BrokerCancelRequest(string? Reason);

/// <summary>R64: the broker's settings.</summary>
public sealed record BrokerSettingsItem(string Mode, int FillPercent, string? RejectReason, decimal SlippagePercent, int SettlementDays)
{
    public static BrokerSettingsItem From(BrokerSettings settings) => new(
        settings.Mode.ToString(),
        settings.FillPercent,
        settings.RejectReason?.ToString(),
        settings.SlippagePercent,
        settings.SettlementDays);
}

/// <summary>R64: an order in the simulator broker's books.</summary>
public sealed record BrokerOrderItem(
    string BrokerOrderId,
    string ClientOrderId,
    string Symbol,
    string Currency,
    string Side,
    decimal Quantity,
    decimal PriceLimit,
    decimal ReferencePrice,
    string Status,
    string? RejectReason,
    decimal FilledQuantity,
    DateOnly? AcceptedDay)
{
    public static BrokerOrderItem From(BrokerOrder order, Guid worldId) => new(
        BrokerOrder.BrokerOrderId(worldId, order.Number),
        order.ClientOrderId,
        order.Symbol,
        order.Currency.Trim(),
        order.Side.ToString(),
        order.Quantity,
        order.PriceLimit,
        order.ReferencePrice,
        order.Status.ToString(),
        order.RejectReason?.ToString(),
        order.FilledQuantity,
        order.AcceptedDay);
}

public sealed record BrokerOrderPage(IReadOnlyList<BrokerOrderItem> Items, int Page, int PageSize, int TotalCount);

public sealed record BrokerFillItem(
    decimal Quantity,
    decimal Price,
    DateOnly TradeDate,
    DateOnly SettlementDate,
    string ContractNote,
    bool Settled);

public sealed record BrokerReportItem(string Id, string Kind, DateOnly? SettledDate, string? Reason);

/// <summary>R64: one order with its fills and the reports it sent.</summary>
public sealed record BrokerOrderDetail(BrokerOrderItem Order, IReadOnlyList<BrokerFillItem> Fills, IReadOnlyList<BrokerReportItem> Reports);
