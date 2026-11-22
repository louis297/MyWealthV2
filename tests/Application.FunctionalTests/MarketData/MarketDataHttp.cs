using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.MarketData;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.MarketData;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Data.Entities;

namespace MyWealthV2.Application.FunctionalTests.MarketData;

/// <summary>Helpers for the market data connection tests (simulated-instruments §10).</summary>
internal static class MarketDataHttp
{
    public const string Connection = "simulator";

    /// <summary>The functional host's market data connections: one connection, <c>simulator</c>, suffix <c>.SIM</c>.</summary>
    public static MarketDataConnections Connections { get; } =
        new([new MarketDataConnection(Connection, "Simulator", ".SIM")]);

    public static InMemoryMarketPrices Prices =>
        FunctionalTestSetup.WebApiServices.GetRequiredService<InMemoryMarketPrices>();

    public static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>Runs the connection's price read as the worker does, at <paramref name="at"/> (real time when null).</summary>
    public static async Task<MarketPricesOutcome> RunAsync(DateTimeOffset? at = null)
    {
        await using var scope = FunctionalTestSetup.WebApiServices.CreateAsyncScope();
        var service = at is null
            ? scope.ServiceProvider.GetRequiredService<SyncMarketPrices>()
            : ActivatorUtilities.CreateInstance<SyncMarketPrices>(scope.ServiceProvider, (TimeProvider)new FixedTime(at.Value));
        return await service.RunAsync(Connection);
    }

    public static MarketPriceItem Item(string symbol, string currency, decimal price, DateOnly? date = null) =>
        new(symbol, currency, price, date ?? new DateOnly(2031, 5, 6));

    /// <summary>Adds an instrument to <paramref name="tenantId"/>'s catalog straight in the table.</summary>
    public static async Task<Instrument> AddInstrumentAsync(int tenantId, string symbol, string currency, bool active = true)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var instrument = Instrument.Create(tenantId, symbol, symbol + " name", Currency.Create(currency, currency, 2));
        if (!active)
        {
            instrument.Disable();
        }

        instrument.CreatedBy = "test";
        db.Instruments.Add(instrument);
        await db.SaveChangesAsync();
        return instrument;
    }

    public static async Task<List<InstrumentPrice>> PricesOfAsync(int instrumentId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.InstrumentPrices.AsNoTracking()
            .Where(row => row.InstrumentId == instrumentId)
            .OrderBy(row => row.AsOfDate)
            .ToListAsync();
    }

    public static async Task<MarketDataFeed?> FeedAsync()
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.MarketDataFeeds.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Connection == Connection && row.Kind == MarketDataFeedKind.Prices);
    }

    private sealed class FixedTime(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
