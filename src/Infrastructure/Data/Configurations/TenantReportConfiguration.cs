using MyWealthV2.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MyWealthV2.Infrastructure.Data.Configurations;

public class TenantReportConfiguration : IEntityTypeConfiguration<TenantReport>
{
    public void Configure(EntityTypeBuilder<TenantReport> builder)
    {
        builder.ToTable("TenantReports");
        builder.HasIndex(grant => new { grant.TenantId, grant.ReportId }).IsUnique();
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(grant => grant.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Report>()
            .WithMany()
            .HasForeignKey(grant => grant.ReportId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
