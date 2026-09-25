using MyWealthV2.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MyWealthV2.Infrastructure.Data.Configurations;

public class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable("Transactions");
        builder.Property(transaction => transaction.PublicId).IsRequired();
        builder.Property(transaction => transaction.Memo).HasMaxLength(200);
        builder.Property(transaction => transaction.Reference).HasMaxLength(100);
        builder.Property(transaction => transaction.CreatedBy).HasMaxLength(450).IsRequired();
        builder.Property(transaction => transaction.RowVersion).IsRowVersion();
        builder.HasIndex(transaction => transaction.PublicId).IsUnique();
        builder.HasIndex(transaction => new { transaction.TenantId, transaction.AccountId, transaction.BookedAt });
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(transaction => transaction.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(transaction => transaction.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Transaction>()
            .WithMany()
            .HasForeignKey(transaction => transaction.OriginalTransactionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(transaction => transaction.CashLeg)
            .WithOne()
            .HasForeignKey<TransactionCashLeg>(leg => leg.TransactionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
