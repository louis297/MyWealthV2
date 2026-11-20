using Microsoft.EntityFrameworkCore;
using MyWealthV2.Simulator.Bank;
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

    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();

    public DbSet<BankLine> BankLines => Set<BankLine>();

    public DbSet<BankFeedPlace> BankFeedPlaces => Set<BankFeedPlace>();

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

        modelBuilder.Entity<BankAccount>(account =>
        {
            account.ToTable("BankAccounts");
            account.HasKey(row => row.Id);
            account.Property(row => row.AccountNumber).HasMaxLength(BankRules.AccountNumberMaxLength);
            account.Property(row => row.Currency).HasColumnType("char(3)").IsFixedLength();
            account.Property(row => row.Name).HasMaxLength(BankRules.NameMaxLength);
        });

        modelBuilder.Entity<BankLine>(line =>
        {
            line.ToTable("BankLines");
            line.HasKey(row => row.Id);
            line.Property(row => row.OutsideId).HasMaxLength(100);
            line.Property(row => row.Amount).HasColumnType("decimal(18,4)");
            line.Property(row => row.PayerName).HasMaxLength(BankRules.FreeTextMaxLength);
            line.Property(row => row.PayerAccount).HasMaxLength(BankRules.AccountNumberMaxLength);
            line.Property(row => row.Reference).HasMaxLength(BankRules.FreeTextMaxLength);
        });

        modelBuilder.Entity<BankFeedPlace>(place =>
        {
            place.ToTable("BankFeedPlaces");
            place.HasKey(row => row.Place);
        });
    }
}
