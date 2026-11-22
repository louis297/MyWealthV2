using Microsoft.EntityFrameworkCore;
using MyWealthV2.Simulator.Data;

namespace MyWealthV2.Simulator.Market;

/// <summary>Simulated instruments R21: the simulator market on the party surface. The key (simulator R30) and the
/// outage (R27) are checked by the party group.</summary>
public static class MarketPartyEndpoints
{
    public static RouteGroupBuilder MapMarketPartyEndpoints(this RouteGroupBuilder party)
    {
        party.MapGet("/market/prices", GetPricesAsync);
        return party;
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
