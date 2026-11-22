using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using MyWealthV2.Simulator.Data;
using MyWealthV2.Simulator.Market;

namespace MyWealthV2.Simulator.Broker;

/// <summary>Orders R53, R54: the simulator broker on the party surface. The key (simulator R30) and the outage (R27)
/// are checked by the party group.</summary>
public static class BrokerPartyEndpoints
{
    public static RouteGroupBuilder MapBrokerPartyEndpoints(this RouteGroupBuilder party)
    {
        party.MapPost("/broker/orders", PlaceAsync);
        return party;
    }

    /// <summary>
    /// R53–R57: one order. A body outside the field rules → 400; the same client order id and body again → the first
    /// answer; the same id with another body → 409. The order mode decides the answer (R55); mode <c>Fill</c> fills
    /// at once.
    /// </summary>
    private static async Task<IResult> PlaceAsync(
        BrokerOrderRequest body,
        SimulatorDbContext db,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (!BrokerRules.IsOrder(body))
        {
            return Results.BadRequest();
        }

        var world = await BrokerBook.UpToDateAsync(db, time, cancellationToken);
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
                ? BrokerOrder.Reject(number, body.ClientOrderId!, hash, body.Symbol!, body.Currency!, side, quantity, limit, reference, rejected)
                : BrokerOrder.Accept(number, body.ClientOrderId!, hash, body.Symbol!, body.Currency!, side, quantity, limit, reference, day);
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
        })));

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
    decimal? ReferencePrice);

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
