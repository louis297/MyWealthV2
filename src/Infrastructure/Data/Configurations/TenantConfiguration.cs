using MyWealthV2.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MyWealthV2.Infrastructure.Data.Configurations;

public class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("Tenants");
        builder.Property(tenant => tenant.Name).HasMaxLength(200).IsRequired();
        builder.Property(tenant => tenant.Code).HasMaxLength(50).IsRequired();
        builder.Property(tenant => tenant.PublicId).IsRequired();
        builder.Property(tenant => tenant.RowVersion).IsRowVersion();
        builder.HasIndex(tenant => tenant.PublicId).IsUnique();
        builder.HasIndex(tenant => tenant.Name).IsUnique();
        builder.HasIndex(tenant => tenant.Code).IsUnique();
    }
}
