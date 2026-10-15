using MyWealthV2.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MyWealthV2.Infrastructure.Data.Configurations;

public class ReportConfiguration : IEntityTypeConfiguration<Report>
{
    public void Configure(EntityTypeBuilder<Report> builder)
    {
        builder.ToTable("Reports");
        builder.Property(report => report.PublicId).IsRequired();
        builder.Property(report => report.Code).HasMaxLength(64).IsRequired();
        builder.Property(report => report.Name).HasMaxLength(200).IsRequired();
        builder.Property(report => report.Function).HasMaxLength(500).IsRequired();
        builder.Property(report => report.Scope).HasMaxLength(20).IsRequired();
        builder.Property(report => report.AllowedTriggers).HasMaxLength(200).IsRequired();
        builder.Property(report => report.IsActive).IsRequired();
        builder.HasIndex(report => report.PublicId).IsUnique();
        builder.HasIndex(report => report.Code).IsUnique();
    }
}
