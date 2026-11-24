using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Infrastructure.Data.Configurations;

/// <summary>In-kind transfers §6.2: custodian reconciliation runs (script 0056).</summary>
public class CustodianReconciliationConfiguration : IEntityTypeConfiguration<CustodianReconciliation>
{
    public void Configure(EntityTypeBuilder<CustodianReconciliation> builder)
    {
        builder.ToTable("CustodianReconciliations");
        builder.Property(run => run.PublicId).IsRequired();
        builder.Property(run => run.Currency).HasColumnType("char(3)").IsFixedLength().IsRequired();
        builder.Property(run => run.CreatedBy).HasMaxLength(450).IsRequired();
        builder.HasIndex(run => run.PublicId).IsUnique();
        builder.HasMany(run => run.Lines)
            .WithOne()
            .HasForeignKey(line => line.ReconciliationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(run => run.Lines).HasField("_lines").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasOne<SafekeepingAccount>()
            .WithMany()
            .HasForeignKey(run => run.SafekeepingAccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>In-kind transfers §6.2: the lines of a custodian reconciliation run (script 0056).</summary>
public class CustodianReconciliationLineConfiguration : IEntityTypeConfiguration<CustodianReconciliationLine>
{
    public void Configure(EntityTypeBuilder<CustodianReconciliationLine> builder)
    {
        builder.ToTable("CustodianReconciliationLines");
        builder.Property(line => line.Symbol).HasMaxLength(CustodianPosition.SymbolMaxLength).IsRequired();
        foreach (var property in new[]
                 {
                     nameof(CustodianReconciliationLine.CustodianQuantity), nameof(CustodianReconciliationLine.PlatformQuantity),
                     nameof(CustodianReconciliationLine.Difference), nameof(CustodianReconciliationLine.NotProcessed),
                     nameof(CustodianReconciliationLine.UnsettledTrades), nameof(CustodianReconciliationLine.Suspense),
                     nameof(CustodianReconciliationLine.UnbookedFills), nameof(CustodianReconciliationLine.Unexplained)
                 })
        {
            builder.Property<decimal>(property).HasColumnType("decimal(26,8)");
        }
    }
}
