using Microsoft.EntityFrameworkCore;
using MyWealthV2.Simulator.Data;
using MyWealthV2.Simulator.Worlds;

namespace MyWealthV2.Simulator.Market;

/// <summary>Simulated instruments R17, R18: one price per simulator day, each a seeded step from the day before.</summary>
public static class MarketPricePath
{
    public static readonly DateOnly Epoch = new(2000, 1, 1);

    /// <summary>The simulator day of <paramref name="simulatorNow"/>: its UTC date.</summary>
    public static DateOnly Day(DateTimeOffset simulatorNow) => DateOnly.FromDateTime(simulatorNow.UtcDateTime);

    /// <summary>R17: <c>Max(0.0001, Round(previous × (1 + move), 4))</c>, rounded half away from zero, with
    /// <c>move = dailyMovePercent / 100 × (2u − 1)</c> and <c>u</c> the first draw of (seed,
    /// <c>market-price:{symbol}</c>, the day number of <paramref name="day"/> from 2000-01-01).</summary>
    public static decimal Step(decimal previous, decimal dailyMovePercent, int seed, string symbol, DateOnly day)
    {
        var n = day.DayNumber - Epoch.DayNumber;
        var u = (decimal)SeededRandom.For(seed, $"market-price:{symbol}", n).NextDouble();
        var move = dailyMovePercent / 100m * (2m * u - 1m);
        return Math.Max(0.0001m, Math.Round(previous * (1m + move), 4, MidpointRounding.AwayFromZero));
    }

    /// <summary>
    /// R18: stores the price of every day from each instrument's last priced day up to <paramref name="today"/>, in
    /// day order, under a lock on the instrument row, so two requests at once step a day once. Leaves the change
    /// tracker empty.
    /// </summary>
    public static async Task BringUpToDateAsync(SimulatorDbContext db, int seed, DateOnly today, CancellationToken cancellationToken)
    {
        var behind = await db.MarketInstruments.AsNoTracking()
            .Where(instrument => instrument.PriceDate < today)
            .Select(instrument => instrument.Id)
            .ToListAsync(cancellationToken);
        foreach (var id in behind)
        {
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT 1 FROM [MarketInstruments] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {id}", cancellationToken);
                var instrument = await db.MarketInstruments.SingleAsync(row => row.Id == id, cancellationToken);
                for (var day = instrument.PriceDate.AddDays(1); day <= today; day = day.AddDays(1))
                {
                    var price = Step(instrument.Price, instrument.DailyMovePercent, seed, instrument.Symbol, day);
                    db.MarketPrices.Add(instrument.StepTo(day, price));
                }

                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            });
        }

        db.ChangeTracker.Clear();
    }
}
