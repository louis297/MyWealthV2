using Microsoft.EntityFrameworkCore;
using MyWealthV2.Simulator.Worlds;

namespace MyWealthV2.Simulator.Data;

/// <summary>
/// The simulator's own books on <c>MyWealthSimulatorDb</c> (simulator spec R14, §6.2). Mapping only: the
/// scripts in <c>database/simulator/</c> own the schema.
/// </summary>
public sealed class SimulatorDbContext(DbContextOptions<SimulatorDbContext> options) : DbContext(options)
{
    public DbSet<World> Worlds => Set<World>();

    public DbSet<ControlLogEntry> ControlLog => Set<ControlLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<World>(world =>
        {
            world.ToTable("World");
            world.HasKey(row => row.Id);
            world.Property(row => row.Id).ValueGeneratedNever();
            world.Property(row => row.RowVersion).IsRowVersion();
            world.Ignore(row => row.Paused);
        });

        modelBuilder.Entity<ControlLogEntry>(entry =>
        {
            entry.ToTable("ControlLog");
            entry.HasKey(row => row.Id);
            entry.Property(row => row.Actor).HasMaxLength(256);
            entry.Property(row => row.Action).HasMaxLength(50);
        });
    }
}
