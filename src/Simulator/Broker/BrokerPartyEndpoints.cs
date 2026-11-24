using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using MyWealthV2.Simulator.Bank;
using MyWealthV2.Simulator.Data;
using MyWealthV2.Simulator.Market;

namespace MyWealthV2.Simulator.Broker;

/// <summary>Orders R53, R54, R59, R63: the simulator broker on the party surface. The key (simulator R30) and the outage (R27)
/// are checked by the party group.</summary>
public static class BrokerPartyEndpoints
{
    public static RouteGroupBuilder MapBrokerPartyEndpoints(this RouteGroupBuilder party)
    {
        party.MapPost("/broker/orders", PlaceAsync);
        party.MapPost("/broker/orders/{clientOrderId}/cancel", CancelAsync);
        party.MapGet("/broker/reports", GetReportsAsync);
        return party;
    }

    public const int DefaultLimit = 100;
    public const int MaxLimit = 100;

    /// <summary>
    /// R63: the reports after the cursor, oldest first; one feed for the whole broker. The cursor is
    /// <c>{WorldId:N}:{place}</c> as on the bank feed (deposit-matching R44); one of another world reads the current
    /// world from its start. The books are brought up to date first, so an expiry or a settlement made by a clock
    /// advance is in the answer.
    /// </summary>
    private static async Task<IResult> GetReportsAsync(
        string? after,
        int? limit,
        BrokerBankAccounts accounts,
        SimulatorDbContext db,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var world = await BrokerBook.UpToDateAsync(db, time, accounts, cancellationToken);
        if (!BankFeedCursor.TryRead(after, world.WorldId, out var afterPlace))
        {
            return Results.BadRequest();
        }

        var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        var rows = await (
                from report in db.BrokerReports.AsNoTracking()
                join order in db.BrokerOrders.AsNoTracking() on report.OrderId equals order.Id into orders
                from order in orders.DefaultIfEmpty()
                where report.Id > afterPlace
                orderby report.Id
                select new
                {
                    report,
                    ClientOrderId = order == null ? null : order.ClientOrderId,
                    OrderNumber = order == null ? (int?)null : order.Number
                })
            .Take(take + 1)
            .ToListAsync(cancellationToken);
        var page = rows.Take(take).ToList();
        var last = page.Count > 0 ? page[^1].report.Id : afterPlace;

        // R63: n counts the reports of the world from 1; no report is ever removed, so the page counts on from its
        // first item.
        var firstNumber = page.Count > 0
            ? await db.BrokerReports.CountAsync(report => report.Id <= page[0].report.Id, cancellationToken)
            : 0;
        var fillIds = page.Where(row => row.report.FillId is not null).Select(row => row.report.FillId!.Value).Distinct().ToList();
        var fills = await db.BrokerFills.AsNoTracking()
            .Where(fill => fillIds.Contains(fill.Id))
            .ToDictionaryAsync(fill => fill.Id, cancellationToken);
        // The number of each fill's own Fill report, which a Settled report names.
        var settlementIds = page.Where(row => row.report.SettlementId is not null)
            .Select(row => row.report.SettlementId!.Value).Distinct().ToList();
        var settlements = await db.BrokerSettlements.AsNoTracking()
            .Where(settlement => settlementIds.Contains(settlement.Id))
            .ToDictionaryAsync(settlement => settlement.Id, cancellationToken);
        var fillReports = await db.BrokerReports.AsNoTracking()
            .Where(report => report.Kind == BrokerReportKind.Fill && fillIds.Contains(report.FillId!.Value))
            .Select(report => new { FillId = report.FillId!.Value, Number = db.BrokerReports.Count(other => other.Id <= report.Id) })
            .ToDictionaryAsync(report => report.FillId, report => report.Number, cancellationToken);

        var items = page.Select((row, index) =>
        {
            var report = row.report;
            var fill = report.Kind == BrokerReportKind.Fill ? fills[report.FillId!.Value] : null;
            var settlement = report.SettlementId is int settlementId ? settlements[settlementId] : null;
            var closing = report.Kind == BrokerReportKind.Settlement ? settlement : null;
            return new BrokerFeedItem(
                BrokerBook.ReportId(world.WorldId, firstNumber + index),
                report.Kind.ToString(),
                row.ClientOrderId,
                row.OrderNumber is int orderNumber ? BrokerOrder.BrokerOrderId(world.WorldId, orderNumber) : null,
                fill?.Quantity,
                fill?.Price,
                fill?.TradeDate,
                fill?.SettlementDate,
                fill?.ContractNote,
                report.Kind is BrokerReportKind.Settled or BrokerReportKind.Broken
                    ? BrokerBook.ReportId(world.WorldId, fillReports[report.FillId!.Value])
                    : null,
                closing?.SettlementDate ?? report.SettledDate,
                report.Reason,
                settlement is null ? null : BrokerSettlement.SettlementId(world.WorldId, settlement.Number),
                closing?.SettlementAccount,
                closing?.Currency.Trim(),
                closing?.NetAmount,
                closing?.FillCount);
        }).ToList();
        return Results.Ok(new BrokerFeed(items, BankFeedCursor.Format(world.WorldId, last), rows.Count > take));
    }

    /// <summary>
    /// R53–R57: one order. A body outside the field rules → 400; the same client order id and body again → the first
    /// answer; the same id with another body → 409. The order mode decides the answer (R55); mode <c>Fill</c> fills
    /// at once.
    /// </summary>
    private static async Task<IResult> PlaceAsync(
        BrokerOrderRequest body,
        BrokerBankAccounts accounts,
        SimulatorDbContext db,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (!BrokerRules.IsOrder(body))
        {
            return Results.BadRequest();
        }

        var world = await BrokerBook.UpToDateAsync(db, time, accounts, cancellationToken);
        var day = MarketPricePath.Day(world.Now(time.GetUtcNow()));
        var hash = Hash(body);
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await BrokerBook.LockAsync(db, cancellationToken);
            var known = await db.BrokerOrders.AsNoTracking()
                .SingleOrDefaultAsync(row => row.ClientOrderId == body.ClientOrderId, cancellationToken);
            if (known is not null)
            {
                return CryptographicOperations.FixedTimeEquals(known.BodyHash, hash)
                    ? Results.Ok(BrokerOrderAnswer.From(known, world.WorldId))
                    : Results.StatusCode(StatusCodes.Status409Conflict);
            }

            var settings = await db.BrokerSettings.AsNoTracking().SingleAsync(cancellationToken);
            var number = (await db.BrokerOrders.MaxAsync(row => (int?)row.Number, cancellationToken) ?? 0) + 1;
            var side = Enum.Parse<BrokerOrderSide>(body.Side!);
            var (quantity, limit, reference) = (body.Quantity!.Value, body.PriceLimit!.Value, body.ReferencePrice!.Value);

            // R54: a simulated instrument trades in its own currency only, whatever the order mode.
            var instrument = await db.MarketInstruments.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Symbol == body.Symbol, cancellationToken);
            BrokerRejectReason? reason = instrument is not null && instrument.Currency.Trim() != body.Currency
                ? BrokerRejectReason.UnknownInstrument
                : settings.Mode == BrokerOrderMode.Reject
                    ? settings.RejectReason
                    : null;
            var order = reason is { } rejected
                ? BrokerOrder.Reject(
                    number, body.ClientOrderId!, hash, body.Symbol!, body.Currency!, side, quantity, limit, reference, rejected,
                    body.SettlementAccount?.Trim(), body.SafekeepingAccount?.Trim())
                : BrokerOrder.Accept(
                    number, body.ClientOrderId!, hash, body.Symbol!, body.Currency!, side, quantity, limit, reference, day,
                    body.SettlementAccount?.Trim(), body.SafekeepingAccount?.Trim());
            db.BrokerOrders.Add(order);
            await db.SaveChangesAsync(cancellationToken);

            if (order.Status == BrokerOrderStatus.Working && settings.Mode == BrokerOrderMode.Fill)
            {
                var part = BrokerRules.FilledPart(order.Quantity, settings.FillPercent);
                if (part > 0)
                {
                    await BrokerBook.FillAsync(db, world, settings, order, part, day, cancellationToken);
                }
            }

            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(BrokerOrderAnswer.From(order, world.WorldId));
        });
    }

    /// <summary>
    /// R59: the platform asks to cancel an order by its client order id. A working order is cancelled at once, with a
    /// <c>Cancelled</c> report of reason <c>Requested</c>; an order that has ended is too late; an id the broker does
    /// not hold → 404. Not a control action, so nothing is logged.
    /// </summary>
    private static async Task<IResult> CancelAsync(
        string clientOrderId,
        BrokerBankAccounts accounts,
        SimulatorDbContext db,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        await BrokerBook.UpToDateAsync(db, time, accounts, cancellationToken);
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await BrokerBook.LockAsync(db, cancellationToken);
            var order = await db.BrokerOrders.SingleOrDefaultAsync(row => row.ClientOrderId == clientOrderId, cancellationToken);
            if (order is null)
            {
                return Results.NotFound();
            }

            if (order.Status != BrokerOrderStatus.Working)
            {
                return Results.Ok(new BrokerCancelAnswer("TooLate"));
            }

            await BrokerBook.CancelAsync(db, order, BrokerRules.RequestedReason, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(new BrokerCancelAnswer("Accepted"));
        });
    }

    /// <summary>R54: the same order, whatever the scale its numbers were written with.</summary>
    private static byte[] Hash(BrokerOrderRequest body) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new object?[]
        {
            body.ClientOrderId,
            body.Symbol,
            body.Currency,
            body.Side,
            Number(body.Quantity!.Value),
            body.Type,
            body.TimeInForce,
            Number(body.PriceLimit!.Value),
            Number(body.ReferencePrice!.Value)
        }.Concat(body.SettlementAccount is null ? [] : new object?[] { body.SettlementAccount })
            // In-kind-transfers R67: part of the comparison only when it is sent.
            .Concat(body.SafekeepingAccount is null ? [] : new object?[] { "safekeepingAccount", body.SafekeepingAccount }).ToArray())));

    private static string Number(decimal value) => value.ToString("0.########", CultureInfo.InvariantCulture);
}

public sealed record BrokerOrderRequest(
    string? ClientOrderId,
    string? Symbol,
    string? Currency,
    string? Side,
    decimal? Quantity,
    string? Type,
    string? TimeInForce,
    decimal? PriceLimit,
    decimal? ReferencePrice,
    string? SettlementAccount = null,
    string? SafekeepingAccount = null);

/// <summary>R63: one page of the broker's feed.</summary>
public sealed record BrokerFeed(IReadOnlyList<BrokerFeedItem> Items, string Cursor, bool HasMore);

/// <summary>R63: a report on the wire. A field a kind does not use is null.</summary>
public sealed record BrokerFeedItem(
    string Id,
    string Kind,
    string? ClientOrderId,
    string? BrokerOrderId,
    decimal? Quantity,
    decimal? Price,
    DateOnly? TradeDate,
    DateOnly? SettlementDate,
    string? ContractNote,
    string? FillId,
    DateOnly? SettledDate,
    string? Reason,
    string? SettlementId,
    string? SettlementAccount,
    string? Currency,
    decimal? NetAmount,
    int? FillCount);

/// <summary>R59: <c>Accepted</c> or <c>TooLate</c>.</summary>
public sealed record BrokerCancelAnswer(string Status);

/// <summary>R54: <c>Accepted</c> with the broker's order id, or <c>Rejected</c> with the reason.</summary>
public sealed record BrokerOrderAnswer(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? BrokerOrderId,
    string Status,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Reason)
{
    public static BrokerOrderAnswer From(BrokerOrder order, Guid worldId) =>
        order.Status == BrokerOrderStatus.Rejected
            ? new BrokerOrderAnswer(null, "Rejected", order.RejectReason.ToString())
            : new BrokerOrderAnswer(BrokerOrder.BrokerOrderId(worldId, order.Number), "Accepted", null);
}
