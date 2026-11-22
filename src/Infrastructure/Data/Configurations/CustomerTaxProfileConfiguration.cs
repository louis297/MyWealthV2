using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Infrastructure.Data.Configurations;

/// <summary>Interest run §6.1: one tax profile per Customer, keyed by the Customer (script 0047).</summary>
public class CustomerTaxProfileConfiguration : IEntityTypeConfiguration<CustomerTaxProfile>
{
    public void Configure(EntityTypeBuilder<CustomerTaxProfile> builder)
    {
        builder.ToTable("CustomerTaxProfiles");
        builder.Ignore(profile => profile.Id);
        builder.Ignore(profile => profile.Withholding);
        builder.HasKey(profile => profile.UserId);
        builder.Property(profile => profile.UserId).ValueGeneratedNever();
        builder.Property(profile => profile.RwtRatePercent).HasPrecision(5, 2);
        builder.Property(profile => profile.CreatedBy).HasMaxLength(450).IsRequired();
        builder.Property(profile => profile.RowVersion).IsRowVersion();
    }
}
