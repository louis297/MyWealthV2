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
        // Transaction source §6: two columns, no FK; Source is built from them.
        builder.Ignore(transaction => transaction.Source);
        builder.Property(transaction => transaction.SourceKind).IsRequired();
        builder.Property(transaction => transaction.SourcePublicId);
        builder.HasIndex(transaction => transaction.PublicId).IsUnique();
        builder.HasIndex(transaction => new { transaction.TenantId, transaction.BookedAt });
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(transaction => transaction.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Transaction>()
            .WithMany()
            .HasForeignKey(transaction => transaction.OriginalTransactionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Transaction>("ReversedBy")
            .WithMany()
            .HasForeignKey(transaction => transaction.ReversedByTransactionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(transaction => transaction.ReversedByTransactionId)
            .IsUnique()
            .HasFilter("[ReversedByTransactionId] IS NOT NULL");
        builder.HasMany(transaction => transaction.CashLegs)
            .WithOne()
            .HasForeignKey(leg => leg.TransactionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(transaction => transaction.SecurityLegs)
            .WithOne()
            .HasForeignKey(leg => leg.TransactionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(transaction => transaction.FxDetail)
            .WithOne()
            .HasForeignKey<TransactionFxDetail>(detail => detail.TransactionId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_TransactionFxDetails_Transactions_TransactionId");
        builder.HasOne(transaction => transaction.AdjustmentDetail)
            .WithOne()
            .HasForeignKey<TransactionAdjustmentDetail>(detail => detail.TransactionId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_TransactionAdjustmentDetails_Transactions_TenantId_TransactionId");
        builder.HasOne(transaction => transaction.BankReceiptDetail)
            .WithOne()
            .HasForeignKey<TransactionBankReceiptDetail>(detail => detail.TransactionId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_TransactionBankReceiptDetails_Transactions_TenantId_TransactionId");
        builder.HasOne(transaction => transaction.PaymentDetail)
            .WithOne()
            .HasForeignKey<TransactionPaymentDetail>(detail => detail.TransactionId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_TransactionPaymentDetails_Transactions_TenantId_TransactionId");
        builder.HasMany(transaction => transaction.FeeDetails)
            .WithOne()
            .HasForeignKey(detail => detail.TransactionId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_TransactionFeeDetails_Transactions_TenantId_TransactionId");
        builder.HasMany(transaction => transaction.TaxDetails)
            .WithOne()
            .HasForeignKey(detail => detail.TransactionId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_TransactionTaxDetails_Transactions_TenantId_TransactionId");
        builder.HasOne(transaction => transaction.InterestDetail)
            .WithOne()
            .HasForeignKey<TransactionInterestDetail>(detail => detail.TransactionId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_TransactionInterestDetails_Transactions_TenantId_TransactionId");
        builder.HasOne(transaction => transaction.CorporateActionDetail)
            .WithOne()
            .HasForeignKey<TransactionCorporateActionDetail>(detail => detail.TransactionId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_TransactionCorporateActionDetails_Transactions_TenantId_TransactionId");
    }
}
