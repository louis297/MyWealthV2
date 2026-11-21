using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Infrastructure.Data.Configurations;

/// <summary>Account closure §6: the account closures of Custodial accounts (script 0046).</summary>
public class AccountClosureConfiguration : IEntityTypeConfiguration<AccountClosure>
{
    public void Configure(EntityTypeBuilder<AccountClosure> builder)
    {
        builder.ToTable("AccountClosures");
        builder.Ignore(row => row.IsInProgress);
        builder.Property(row => row.PublicId).IsRequired();
        builder.Property(row => row.Memo).HasMaxLength(AccountClosure.MemoMaxLength).IsRequired();
        builder.Property(row => row.Currency).HasColumnType("char(3)").IsFixedLength().IsRequired();
        builder.Property(row => row.CashBalanceAtRequest).HasColumnType("decimal(18,4)");
        builder.Property(row => row.PayoutAmount).HasColumnType("decimal(18,4)");
        builder.Property(row => row.WriteOffAmount).HasColumnType("decimal(18,4)");
        builder.Property(row => row.CreatedBy).HasMaxLength(450).IsRequired();
        builder.Property(row => row.RowVersion).IsRowVersion();
        builder.HasIndex(row => row.PublicId).IsUnique();
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(row => row.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
