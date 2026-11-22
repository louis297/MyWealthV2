using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Infrastructure.Data.Configurations;

/// <summary>Interest run §6.2: a tenant's interest rate schedules and their bands (script 0048).</summary>
public class InterestRateScheduleConfiguration : IEntityTypeConfiguration<InterestRateSchedule>
{
    public void Configure(EntityTypeBuilder<InterestRateSchedule> builder)
    {
        builder.ToTable("InterestRateSchedules");
        builder.Property(schedule => schedule.PublicId).IsRequired();
        builder.Property(schedule => schedule.Currency).HasColumnType("char(3)").IsFixedLength().HasMaxLength(3).IsRequired();
        builder.Property(schedule => schedule.EffectiveFrom).HasColumnType("date").IsRequired();
        builder.Property(schedule => schedule.CreatedBy).HasMaxLength(450).IsRequired();
        builder.Property(schedule => schedule.RowVersion).IsRowVersion();
        builder.Ignore(schedule => schedule.Bands);
        builder.HasIndex(schedule => schedule.PublicId).IsUnique();
        builder.HasIndex(schedule => new { schedule.TenantId, schedule.Currency, schedule.EffectiveFrom }).IsUnique();
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(schedule => schedule.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Currency>()
            .WithMany()
            .HasForeignKey(schedule => schedule.Currency)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(schedule => schedule.BandRows)
            .WithOne()
            .HasForeignKey(band => band.InterestRateScheduleId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_InterestRateBands_InterestRateSchedules_TenantId_InterestRateScheduleId");
        builder.Navigation(schedule => schedule.BandRows)
            .HasField("_bands")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class InterestRateBandConfiguration : IEntityTypeConfiguration<InterestRateBand>
{
    public void Configure(EntityTypeBuilder<InterestRateBand> builder)
    {
        builder.ToTable("InterestRateBands");
        builder.Property(band => band.FromAmount).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(band => band.RatePercent).HasColumnType("decimal(7,4)").IsRequired();
    }
}
