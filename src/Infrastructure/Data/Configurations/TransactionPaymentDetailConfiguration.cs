using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Infrastructure.Data.Configurations;

/// <summary>Withdrawals §6.2: one row per payout and per its Reversal (script 0043).</summary>
public class TransactionPaymentDetailConfiguration : IEntityTypeConfiguration<TransactionPaymentDetail>
{
    public void Configure(EntityTypeBuilder<TransactionPaymentDetail> builder)
    {
        builder.ToTable("TransactionPaymentDetails");
        builder.HasKey(detail => detail.TransactionId);
        builder.Property(detail => detail.TransactionId).ValueGeneratedNever();
        builder.Property(detail => detail.Reference).HasMaxLength(TransactionPaymentDetail.ReferenceMaxLength);
        builder.Property(detail => detail.PayeeName).HasMaxLength(TransactionPaymentDetail.PayeeNameMaxLength);
        builder.Property(detail => detail.PayeeAccount).HasMaxLength(TransactionPaymentDetail.PayeeAccountMaxLength);
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(detail => detail.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
