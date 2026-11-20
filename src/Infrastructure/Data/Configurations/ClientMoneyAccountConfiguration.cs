using MyWealthV2.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MyWealthV2.Infrastructure.Data.Configurations;

public class ClientMoneyAccountConfiguration : IEntityTypeConfiguration<ClientMoneyAccount>
{
    public void Configure(EntityTypeBuilder<ClientMoneyAccount> builder)
    {
        builder.ToTable("ClientMoneyAccounts");
        builder.Property(row => row.PublicId).IsRequired();
        builder.Property(row => row.Currency).HasColumnType("char(3)").IsFixedLength().IsRequired();
        builder.Property(row => row.BankName).HasMaxLength(ClientMoneyAccount.BankNameMaxLength).IsRequired();
        builder.Property(row => row.AccountName).HasMaxLength(ClientMoneyAccount.AccountNameMaxLength).IsRequired();
        builder.Property(row => row.AccountNumber).HasMaxLength(ClientMoneyAccount.AccountNumberMaxLength).IsRequired();
        builder.Ignore(row => row.BankConnection);
        builder.Property(row => row.IsActive).IsRequired();
        builder.Property(row => row.CreatedBy).HasMaxLength(450).IsRequired();
        builder.Property(row => row.RowVersion).IsRowVersion();
        builder.HasIndex(row => row.PublicId).IsUnique();
        builder.HasIndex(row => new { row.TenantId, row.Currency })
            .IsUnique()
            .HasFilter("[IsActive] = 1")
            .HasDatabaseName("UX_ClientMoneyAccounts_TenantId_Currency_Active");
        builder.HasIndex(row => row.AccountNumber)
            .IsUnique()
            .HasFilter("[IsActive] = 1")
            .HasDatabaseName("UX_ClientMoneyAccounts_AccountNumber_Active");
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
