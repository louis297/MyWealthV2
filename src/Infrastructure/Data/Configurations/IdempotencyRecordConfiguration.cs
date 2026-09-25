using MyWealthV2.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MyWealthV2.Infrastructure.Data.Configurations;

public class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("IdempotencyRecords");
        builder.Property(record => record.Method).HasMaxLength(10).IsRequired();
        builder.Property(record => record.Path).HasMaxLength(200).IsRequired();
        builder.Property(record => record.RequestHash).HasMaxLength(64).IsRequired();
        builder.Property(record => record.ResponseBody).IsRequired();
        builder.HasIndex(record => new { record.TenantId, record.Key }).IsUnique();
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(record => record.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
