using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Infrastructure.Data.Configurations;

/// <summary>Deposit matching §6.2: one row per deposit and per its Reversal (script 0041).</summary>
public class TransactionBankReceiptDetailConfiguration : IEntityTypeConfiguration<TransactionBankReceiptDetail>
{
    public void Configure(EntityTypeBuilder<TransactionBankReceiptDetail> builder)
    {
        builder.ToTable("TransactionBankReceiptDetails");
        builder.HasKey(detail => detail.TransactionId);
        builder.Property(detail => detail.TransactionId).ValueGeneratedNever();
        builder.Property(detail => detail.Reference).HasMaxLength(TransactionBankReceiptDetail.FreeTextMaxLength);
        builder.Property(detail => detail.PayerName).HasMaxLength(TransactionBankReceiptDetail.FreeTextMaxLength);
        builder.Property(detail => detail.PayerAccount).HasMaxLength(TransactionBankReceiptDetail.PayerAccountMaxLength);
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(detail => detail.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
