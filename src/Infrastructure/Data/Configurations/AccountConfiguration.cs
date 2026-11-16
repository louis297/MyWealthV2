using MyWealthV2.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MyWealthV2.Infrastructure.Data.Configurations;

public class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("Accounts");
        builder.Property(account => account.Name).HasMaxLength(200).IsRequired();
        builder.Property(account => account.PublicId).IsRequired();
        builder.Property(account => account.Currency).HasColumnType("char(3)").IsFixedLength().IsRequired();
        builder.Property(account => account.IsActive)
            .HasComputedColumnSql(
                "ISNULL(CAST(CASE WHEN [Status] = 0 THEN 1 ELSE 0 END AS bit), 0)",
                stored: true);
        builder.Property(account => account.CreatedBy).HasMaxLength(450).IsRequired();
        builder.Property(account => account.RowVersion).IsRowVersion();
        builder.HasIndex(account => account.PublicId).IsUnique();
        builder.HasIndex(account => new { account.TenantId, account.CustomerId });
        builder.HasIndex(account => new { account.TenantId, account.IsActive });
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(account => account.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(account => account.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Currency>()
            .WithMany()
            .HasForeignKey(account => account.Currency)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
