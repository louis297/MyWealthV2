using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Infrastructure.Data.Configurations;

public class TransactionFeeDetailConfiguration : IEntityTypeConfiguration<TransactionFeeDetail>
{
    public void Configure(EntityTypeBuilder<TransactionFeeDetail> builder)
    {
        builder.ToTable("TransactionFeeDetails");
        builder.HasKey(detail => detail.CashLegId);
        builder.Property(detail => detail.CashLegId).ValueGeneratedNever();
        builder.Property(detail => detail.Kind).IsRequired();
        builder.Property(detail => detail.BasisAmount).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(detail => detail.FeeAmount).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(detail => detail.Currency).HasColumnType("char(3)").IsFixedLength().HasMaxLength(3).IsRequired();
        builder.HasIndex(detail => new { detail.TransactionId, detail.Kind }).IsUnique();
        builder.HasOne<TradeFeeSchedule>()
            .WithMany()
            .HasForeignKey(detail => detail.TradeFeeScheduleId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_TransactionFeeDetails_TradeFeeSchedules_TenantId_TradeFeeScheduleId");
        builder.HasOne<Currency>()
            .WithMany()
            .HasForeignKey(detail => detail.Currency)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_TransactionFeeDetails_Currencies_Currency");
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(detail => detail.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
