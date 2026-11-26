using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Valuations;

namespace MyWealthV2.Infrastructure.Data.Configurations;

/// <summary>Valuation run §6.1: stored valuation runs (script 0059).</summary>
public class ValuationRunConfiguration : IEntityTypeConfiguration<ValuationRun>
{
    public void Configure(EntityTypeBuilder<ValuationRun> builder)
    {
        builder.ToTable("ValuationRuns");
        builder.Property(run => run.PublicId).IsRequired();
        builder.Property(run => run.ThroughDate).HasColumnType("date").IsRequired();
        builder.Property(run => run.CreatedBy).HasMaxLength(450).IsRequired();
        builder.HasIndex(run => run.PublicId).IsUnique();
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(run => run.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(run => run.RanByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Valuation run §6.1: one stored valuation per Custodial account and UTC day (script 0059).</summary>
public class StoredValuationConfiguration : IEntityTypeConfiguration<StoredValuation>
{
    public void Configure(EntityTypeBuilder<StoredValuation> builder)
    {
        builder.ToTable("StoredValuations");
        builder.HasKey(valuation => valuation.Id);
        builder.Property(valuation => valuation.Id).ValueGeneratedOnAdd();
        builder.Property(valuation => valuation.Date).HasColumnName("ValuationDate").HasColumnType("date").IsRequired();
        builder.Property(valuation => valuation.Currency).HasColumnType("char(3)").IsFixedLength().HasMaxLength(3).IsRequired();
        builder.Property(valuation => valuation.Cash).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(valuation => valuation.HoldingsValue).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(valuation => valuation.Total).HasColumnType("decimal(18,4)").IsRequired();
        builder.HasIndex(valuation => new { valuation.AccountId, valuation.Date }).IsUnique();
        builder.HasIndex(valuation => new { valuation.TenantId, valuation.Date });
        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(valuation => valuation.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ValuationRun>()
            .WithMany()
            .HasForeignKey(valuation => valuation.ValuationRunId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(valuation => valuation.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(valuation => valuation.Lines)
            .WithOne()
            .HasForeignKey(line => line.StoredValuationId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_StoredValuationLines_StoredValuations_TenantId_StoredValuationId");
        builder.Navigation(valuation => valuation.Lines)
            .HasField("_lines")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

/// <summary>Valuation run §6.1: one line per holding of a stored valuation (script 0059).</summary>
public class StoredValuationLineConfiguration : IEntityTypeConfiguration<StoredValuationLine>
{
    public void Configure(EntityTypeBuilder<StoredValuationLine> builder)
    {
        builder.ToTable("StoredValuationLines");
        builder.HasKey(line => line.Id);
        builder.Property(line => line.Id).ValueGeneratedOnAdd();
        builder.Property(line => line.Quantity).HasColumnType("decimal(18,8)").IsRequired();
        builder.Property(line => line.LedgerQuantity).HasColumnType("decimal(18,8)").IsRequired();
        builder.Property(line => line.PriceAmount).HasColumnType("decimal(18,8)");
        builder.Property(line => line.PriceCurrency).HasColumnType("char(3)").IsFixedLength().HasMaxLength(3);
        builder.Property(line => line.PriceDate).HasColumnType("date");
        builder.Property(line => line.PriceSource).HasMaxLength(50);
        builder.Property(line => line.MarketValue).HasColumnType("decimal(18,4)");
        builder.Property(line => line.FxRate).HasColumnType("decimal(18,8)");
        builder.Property(line => line.FxDate).HasColumnType("date");
        builder.Property(line => line.FxSource).HasMaxLength(50);
        builder.Property(line => line.Value).HasColumnType("decimal(18,4)");
        builder.HasIndex(line => new { line.StoredValuationId, line.InstrumentId }).IsUnique();
        builder.HasOne<Instrument>()
            .WithMany()
            .HasForeignKey(line => line.InstrumentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(line => line.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
