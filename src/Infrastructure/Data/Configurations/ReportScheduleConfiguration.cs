using MyWealthV2.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MyWealthV2.Infrastructure.Data.Configurations;

public class ReportScheduleConfiguration : IEntityTypeConfiguration<ReportSchedule>
{
    public void Configure(EntityTypeBuilder<ReportSchedule> builder)
    {
        builder.ToTable("ReportSchedules");
        builder.Property(schedule => schedule.PublicId).IsRequired();
        builder.Property(schedule => schedule.ParametersJson).IsRequired();
        builder.Property(schedule => schedule.Cron).HasMaxLength(100).IsRequired();
        builder.Property(schedule => schedule.CreatedBy).HasMaxLength(450).IsRequired();
        builder.Property(schedule => schedule.RowVersion).IsRowVersion();
        builder.HasIndex(schedule => schedule.PublicId).IsUnique();
        builder.HasIndex(schedule => new { schedule.TenantId, schedule.ReportId });
        builder.HasOne<Report>().WithMany().HasForeignKey(schedule => schedule.ReportId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(schedule => schedule.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
