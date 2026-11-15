using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Infrastructure.Data.Configurations;

public class TradeFeeBandConfiguration : IEntityTypeConfiguration<TradeFeeBand>
{
    public void Configure(EntityTypeBuilder<TradeFeeBand> builder)
    {
        builder.ToTable("TradeFeeBands");
        builder.Property(band => band.FromAmount).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(band => band.RatePercent).HasColumnType("decimal(7,4)").IsRequired();
        builder.HasIndex(band => new { band.TradeFeeScheduleId, band.FromAmount }).IsUnique();
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(band => band.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
