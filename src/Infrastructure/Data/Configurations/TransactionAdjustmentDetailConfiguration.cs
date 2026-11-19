using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Infrastructure.Data.Configurations;

/// <summary>Adjustments §6: one row per <c>Adjustment</c> and per its Reversal (script 0037).</summary>
public class TransactionAdjustmentDetailConfiguration : IEntityTypeConfiguration<TransactionAdjustmentDetail>
{
    public void Configure(EntityTypeBuilder<TransactionAdjustmentDetail> builder)
    {
        builder.ToTable("TransactionAdjustmentDetails");
        builder.HasKey(detail => detail.TransactionId);
        builder.Property(detail => detail.TransactionId).ValueGeneratedNever();
        builder.Property(detail => detail.Reason).IsRequired();
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(detail => detail.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
