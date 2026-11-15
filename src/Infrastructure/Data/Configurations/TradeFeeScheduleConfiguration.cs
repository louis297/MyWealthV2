using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Infrastructure.Data.Configurations;

public class TradeFeeScheduleConfiguration : IEntityTypeConfiguration<TradeFeeSchedule>
{
    public void Configure(EntityTypeBuilder<TradeFeeSchedule> builder)
    {
        builder.ToTable("TradeFeeSchedules");
        builder.Property(schedule => schedule.PublicId).IsRequired();
        builder.Property(schedule => schedule.QuoteCurrency).HasColumnType("char(3)").IsFixedLength().HasMaxLength(3).IsRequired();
        builder.Property(schedule => schedule.EffectiveFrom).HasColumnType("date").IsRequired();
        builder.Property(schedule => schedule.Minimum).HasColumnName("MinimumAmount").HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(schedule => schedule.Cap).HasColumnName("CapAmount").HasColumnType("decimal(18,4)");
        builder.Property(schedule => schedule.CreatedBy).HasMaxLength(450).IsRequired();
        builder.Property(schedule => schedule.RowVersion).IsRowVersion();
        builder.Ignore(schedule => schedule.Bands);
        builder.HasIndex(schedule => schedule.PublicId).IsUnique();
        builder.HasIndex(schedule => new { schedule.TenantId, schedule.QuoteCurrency, schedule.EffectiveFrom }).IsUnique();
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(schedule => schedule.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Currency>()
            .WithMany()
            .HasForeignKey(schedule => schedule.QuoteCurrency)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(schedule => schedule.BandRows)
            .WithOne()
            .HasForeignKey(band => band.TradeFeeScheduleId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_TradeFeeBands_TradeFeeSchedules_TenantId_TradeFeeScheduleId");
        builder.Navigation(schedule => schedule.BandRows)
            .HasField("_bands")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
