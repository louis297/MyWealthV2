using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Infrastructure.Data.Configurations;

/// <summary>Withdrawals §6.1: a Customer's nominated bank accounts (script 0042).</summary>
public class NominatedBankAccountConfiguration : IEntityTypeConfiguration<NominatedBankAccount>
{
    public void Configure(EntityTypeBuilder<NominatedBankAccount> builder)
    {
        builder.ToTable("NominatedBankAccounts");
        builder.Property(row => row.PublicId).IsRequired();
        builder.Property(row => row.Currency).HasColumnType("char(3)").IsFixedLength().IsRequired();
        builder.Property(row => row.BankName).HasMaxLength(NominatedBankAccount.BankNameMaxLength).IsRequired();
        builder.Property(row => row.AccountName).HasMaxLength(NominatedBankAccount.AccountNameMaxLength).IsRequired();
        builder.Property(row => row.AccountNumber).HasMaxLength(NominatedBankAccount.AccountNumberMaxLength).IsRequired();
        builder.Property(row => row.AccountNumberKey).HasMaxLength(NominatedBankAccount.AccountNumberMaxLength).IsRequired();
        builder.Property(row => row.ReviewNote).HasMaxLength(NominatedBankAccount.NoteMaxLength);
        builder.Property(row => row.RemovedNote).HasMaxLength(NominatedBankAccount.NoteMaxLength);
        builder.Property(row => row.CreatedBy).HasMaxLength(450).IsRequired();
        builder.Property(row => row.RowVersion).IsRowVersion();
        builder.HasIndex(row => row.PublicId).IsUnique();
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(row => row.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Currency>()
            .WithMany()
            .HasForeignKey(row => row.Currency)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
