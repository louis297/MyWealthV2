using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Infrastructure.Data.Configurations;

/// <summary>Adjustment review §6: the adjustment requests (script 0058).</summary>
public class AdjustmentRequestConfiguration : IEntityTypeConfiguration<AdjustmentRequest>
{
    public void Configure(EntityTypeBuilder<AdjustmentRequest> builder)
    {
        builder.ToTable("AdjustmentRequests");
        builder.Ignore(row => row.IsOpen);
        builder.Property(row => row.PublicId).IsRequired();
        builder.Property(row => row.Memo).HasMaxLength(AdjustmentRequest.MemoMaxLength).IsRequired();
        builder.Property(row => row.Reference).HasMaxLength(AdjustmentRequest.ReferenceMaxLength);
        builder.Property(row => row.CashAmount).HasColumnType("decimal(18,4)");
        builder.Property(row => row.QuantityChange).HasColumnType("decimal(18,8)");
        builder.Property(row => row.CostChange).HasColumnType("decimal(18,4)");
        builder.Property(row => row.DecisionNote).HasMaxLength(AdjustmentRequest.NoteMaxLength);
        builder.Property(row => row.CreatedBy).HasMaxLength(450).IsRequired();
        builder.Property(row => row.RowVersion).IsRowVersion();
        builder.HasIndex(row => row.PublicId).IsUnique();
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(row => row.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
