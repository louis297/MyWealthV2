using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Infrastructure.Data.Configurations;

/// <summary>Interest run §6.2: one tax detail per <c>Tax</c> cash leg (script 0048).</summary>
public class TransactionTaxDetailConfiguration : IEntityTypeConfiguration<TransactionTaxDetail>
{
    public void Configure(EntityTypeBuilder<TransactionTaxDetail> builder)
    {
        builder.ToTable("TransactionTaxDetails");
        builder.HasKey(detail => detail.CashLegId);
        builder.Property(detail => detail.CashLegId).ValueGeneratedNever();
        builder.Property(detail => detail.Kind).IsRequired();
        builder.Property(detail => detail.RatePercent).HasColumnType("decimal(5,2)").IsRequired();
        builder.Property(detail => detail.BasisAmount).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(detail => detail.TaxAmount).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(detail => detail.Currency).HasColumnType("char(3)").IsFixedLength().HasMaxLength(3).IsRequired();
        builder.HasIndex(detail => new { detail.TransactionId, detail.Kind }).IsUnique();
        builder.HasOne<Currency>()
            .WithMany()
            .HasForeignKey(detail => detail.Currency)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_TransactionTaxDetails_Currencies_Currency");
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(detail => detail.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Interest run §6.2: 0..1 interest detail per Transaction (script 0048).</summary>
public class TransactionInterestDetailConfiguration : IEntityTypeConfiguration<TransactionInterestDetail>
{
    public void Configure(EntityTypeBuilder<TransactionInterestDetail> builder)
    {
        builder.ToTable("TransactionInterestDetails");
        builder.HasKey(detail => detail.TransactionId);
        builder.Property(detail => detail.TransactionId).ValueGeneratedNever();
        builder.Property(detail => detail.PeriodFrom).HasColumnType("date").IsRequired();
        builder.Property(detail => detail.PeriodTo).HasColumnType("date").IsRequired();
        builder.Property(detail => detail.AccruedAmount).HasColumnType("decimal(18,8)").IsRequired();
        builder.Property(detail => detail.GrossAmount).HasColumnType("decimal(18,4)").IsRequired();
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(detail => detail.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Corporate actions §6.1: 0..1 corporate action detail per Transaction (script 0050).</summary>
public class TransactionCorporateActionDetailConfiguration : IEntityTypeConfiguration<TransactionCorporateActionDetail>
{
    public void Configure(EntityTypeBuilder<TransactionCorporateActionDetail> builder)
    {
        builder.ToTable("TransactionCorporateActionDetails");
        builder.HasKey(detail => detail.TransactionId);
        builder.Property(detail => detail.TransactionId).ValueGeneratedNever();
        builder.Property(detail => detail.ExDate).HasColumnType("date").IsRequired();
        builder.Property(detail => detail.RecordDate).HasColumnType("date");
        builder.Property(detail => detail.PaymentDate).HasColumnType("date");
        builder.Property(detail => detail.QuantityBefore).HasColumnType("decimal(18,8)").IsRequired();
        builder.Property(detail => detail.QuantityAfter).HasColumnType("decimal(18,8)").IsRequired();
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(detail => detail.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Orders §6.1: 0..1 execution detail per Transaction (script 0051).</summary>
public class TransactionExecutionDetailConfiguration : IEntityTypeConfiguration<TransactionExecutionDetail>
{
    public void Configure(EntityTypeBuilder<TransactionExecutionDetail> builder)
    {
        builder.ToTable("TransactionExecutionDetails");
        builder.HasKey(detail => detail.TransactionId);
        builder.Property(detail => detail.TransactionId).ValueGeneratedNever();
        builder.Property(detail => detail.Price).HasColumnType("decimal(18,8)").IsRequired();
        builder.Property(detail => detail.TradeDate).HasColumnType("date").IsRequired();
        builder.Property(detail => detail.SettlementDate).HasColumnType("date").IsRequired();
        builder.Property(detail => detail.BrokerReference).HasMaxLength(TransactionExecutionDetail.BrokerReferenceMaxLength);
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(detail => detail.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
