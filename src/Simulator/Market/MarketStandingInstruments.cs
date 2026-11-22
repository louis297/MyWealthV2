using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MyWealthV2.Simulator.Data;

namespace MyWealthV2.Simulator.Market;

public sealed record StandingInstrument(string Symbol, string Name, string Currency, decimal Price, decimal DailyMovePercent);

/// <summary>
/// Simulated instruments R20: instruments the simulator adds when the world is created and after every reset, from
/// <c>Simulator:Market:StandingInstruments</c>. A bad entry stops the host before it listens.
/// </summary>
public sealed class MarketStandingInstruments(IReadOnlyList<StandingInstrument> instruments)
{
    public const string SectionName = "Simulator:Market:StandingInstruments";

    public IReadOnlyList<StandingInstrument> Instruments { get; } = instruments;

    public static MarketStandingInstruments Read(IConfiguration configuration)
    {
        var instruments = new List<StandingInstrument>();
        foreach (var entry in configuration.GetSection(SectionName).GetChildren())
        {
            var symbol = entry["Symbol"];
            var name = entry["Name"];
            var currency = entry["Currency"];
            var price = Decimal(entry["Price"]);
            var dailyMove = string.IsNullOrWhiteSpace(entry["DailyMovePercent"])
                ? MarketRules.DefaultDailyMovePercent
                : Decimal(entry["DailyMovePercent"]);
            if (!MarketRules.IsSymbol(symbol) || !MarketRules.IsName(name) || !MarketRules.IsCurrency(currency)
                || !MarketRules.IsPrice(price) || !MarketRules.IsDailyMove(dailyMove))
            {
                throw new InvalidOperationException(
                    $"{SectionName}:{entry.Key} must have a Symbol (5 to 32 letters, digits, dots and hyphens, ending in " +
                    $"{MarketRules.Suffix}), a Name (1 to 200 characters), a Currency (three upper-case letters), a Price " +
                    "(above zero, at most 4 decimal places) and optionally a DailyMovePercent (0 to 20, at most 2 decimal places).");
            }

            var instrument = new StandingInstrument(MarketRules.Symbol(symbol!), name!.Trim(), currency!, price!.Value, dailyMove!.Value);
            if (instruments.Any(other => other.Symbol == instrument.Symbol))
            {
                throw new InvalidOperationException($"{SectionName} lists {instrument.Symbol} twice.");
            }

            instruments.Add(instrument);
        }

        return new MarketStandingInstruments(instruments);
    }

    /// <summary>Adds the standing instruments that do not exist; one that exists is left as it is. The caller saves.</summary>
    public async Task AddMissingAsync(SimulatorDbContext db, DateOnly today, CancellationToken cancellationToken)
    {
        var existing = await db.MarketInstruments.Select(instrument => instrument.Symbol).ToListAsync(cancellationToken);
        foreach (var instrument in Instruments)
        {
            if (!existing.Contains(instrument.Symbol, StringComparer.OrdinalIgnoreCase))
            {
                db.MarketInstruments.Add(MarketInstrument.Add(
                    instrument.Symbol, instrument.Name, instrument.Currency, instrument.Price, instrument.DailyMovePercent,
                    today, typed: false));
            }
        }
    }

    /// <summary>R20 at start-up: the world exists by now.</summary>
    public static async Task EnsureAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SimulatorDbContext>();
        var time = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        var world = await db.Worlds.AsNoTracking().SingleAsync(cancellationToken);
        await scope.ServiceProvider.GetRequiredService<MarketStandingInstruments>()
            .AddMissingAsync(db, MarketPricePath.Day(world.Now(time.GetUtcNow())), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static decimal? Decimal(string? value) =>
        decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
}
