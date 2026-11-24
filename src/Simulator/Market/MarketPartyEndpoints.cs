using Microsoft.EntityFrameworkCore;
using MyWealthV2.Simulator.Bank;
using MyWealthV2.Simulator.Data;

namespace MyWealthV2.Simulator.Market;

/// <summary>Simulated instruments R21, corporate-actions R44: the simulator market on the party surface. The key (simulator R30) and the
/// outage (R27) are checked by the party group.</summary>
public static class MarketPartyEndpoints
{
    public static RouteGroupBuilder MapMarketPartyEndpoints(this RouteGroupBuilder party)
    {
        party.MapGet("/market/prices", GetPricesAsync);
        party.MapGet("/market/corporate-actions", GetCorporateActionsAsync);
        return party;
    }

    public const int DefaultLimit = 100;
    public const int MaxLimit = 100;

    /// <summary>Corporate-actions R44, R45: the versions after the cursor, oldest first. The cursor is
    /// <c>{WorldId:N}:{place}</c> as on the bank feed (deposit-matching R44); one of another world reads the current world
    /// from its start. The market is brought up to date first, so a version made by a clock advance is in the answer.</summary>
    private static async Task<IResult> GetCorporateActionsAsync(
        string? after,
        int? limit,
        SimulatorDbContext db,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var world = await MarketControlEndpoints.UpToDateAsync(db, time, cancellationToken);
        if (!BankFeedCursor.TryRead(after, world.WorldId, out var afterPlace))
        {
            return Results.BadRequest();
        }

        var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        var rows = await (
                from version in db.MarketCorporateActionVersions.AsNoTracking()
                join action in db.MarketCorporateActions.AsNoTracking() on version.ActionId equals action.Id
                join instrument in db.MarketInstruments.AsNoTracking() on action.InstrumentId equals instrument.Id
                where version.Id > afterPlace
                orderby version.Id
                select new { version, action.Number, action.Kind, instrument.Symbol, instrument.Currency })
            .Take(take + 1)
            .ToListAsync(cancellationToken);
        var page = rows.Take(take).ToList();
        var last = page.Count > 0 ? page[^1].version.Id : afterPlace;
        var items = page.Select(row =>
        {
            var actionId = MarketCorporateAction.ActionId(world.WorldId, row.Number);
            return new MarketCorporateActionFeedItem(
                $"{actionId}-V{row.version.Version.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                actionId,
                row.version.Version,
                row.version.Status.ToString(),
                row.Symbol,
                row.Currency.Trim(),
                row.Kind.ToString(),
                row.version.RatioNew,
                row.version.RatioHeld,
                row.version.ExDate,
                row.version.RecordDate,
                row.version.PaymentDate,
                row.version.AmountPerShare,
                row.version.TaxSource?.ToString(),
                row.version.ImputationCreditPerShare,
                row.version.ForeignTaxRatePercent,
                row.version.ReinvestmentPrice);
        }).ToList();
        return Results.Ok(new MarketCorporateActionFeed(items, BankFeedCursor.Format(world.WorldId, last), rows.Count > take));
    }

    /// <summary>Every instrument, sorted by symbol, with the price of the current simulator day. No platform value
    /// (simulator R33).</summary>
    private static async Task<IResult> GetPricesAsync(SimulatorDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var world = await MarketControlEndpoints.UpToDateAsync(db, time, cancellationToken);
        var instruments = await db.MarketInstruments.AsNoTracking()
            .OrderBy(instrument => instrument.Symbol)
            .ToListAsync(cancellationToken);
        var items = instruments
            .Select(instrument => new MarketPriceFeedItem(instrument.Symbol, instrument.Currency.Trim(), instrument.Price, instrument.PriceDate))
            .ToList();
        return Results.Ok(new MarketPriceFeed(world.Now(time.GetUtcNow()), items));
    }
}

public sealed record MarketPriceFeed(DateTimeOffset AsOf, IReadOnlyList<MarketPriceFeedItem> Items);

public sealed record MarketPriceFeedItem(string Symbol, string Currency, decimal Price, DateOnly Date);

public sealed record MarketCorporateActionFeed(IReadOnlyList<MarketCorporateActionFeedItem> Items, string Cursor, bool HasMore);

public sealed record MarketCorporateActionFeedItem(
    string Id,
    string ActionId,
    int Version,
    string Status,
    string Symbol,
    string Currency,
    string Kind,
    int? RatioNew,
    int? RatioHeld,
    DateOnly ExDate,
    DateOnly? RecordDate,
    DateOnly? PaymentDate,
    decimal? AmountPerShare,
    string? TaxSource,
    decimal? ImputationCreditPerShare,
    decimal? ForeignTaxRatePercent,
    decimal? ReinvestmentPrice);
