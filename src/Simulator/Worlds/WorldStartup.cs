using Microsoft.EntityFrameworkCore;
using MyWealthV2.Simulator.Data;

namespace MyWealthV2.Simulator.Worlds;

public static class WorldStartup
{
    /// <summary>R20: the first start creates the one world with a new id, a random seed, running, no outage.</summary>
    public static async Task EnsureWorldAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SimulatorDbContext>();
        if (await db.Worlds.AnyAsync(cancellationToken))
        {
            return;
        }

        var time = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        db.Worlds.Add(World.Create(Guid.NewGuid(), SeededRandom.NewSeed(), time.GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);
    }
}
