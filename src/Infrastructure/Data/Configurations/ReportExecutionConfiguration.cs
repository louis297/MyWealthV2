using MyWealthV2.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MyWealthV2.Infrastructure.Data.Configurations;

public class ReportExecutionConfiguration : IEntityTypeConfiguration<ReportExecution>
{
    public void Configure(EntityTypeBuilder<ReportExecution> builder)
    {
        builder.ToTable("ReportExecutions");
        builder.Property(execution => execution.PublicId).IsRequired();
        builder.Property(execution => execution.TriggerKind).HasMaxLength(20).IsRequired();
        builder.Property(execution => execution.Status).HasMaxLength(20).IsRequired();
        builder.Property(execution => execution.ParametersJson).IsRequired();
        builder.Property(execution => execution.ResultStorageKey).HasMaxLength(500);
        builder.Property(execution => execution.ErrorMessage).HasMaxLength(2000);
        builder.Property(execution => execution.CreatedBy).HasMaxLength(450).IsRequired();
        builder.Property(execution => execution.RowVersion).IsRowVersion();
        builder.HasIndex(execution => execution.PublicId).IsUnique();
        builder.HasIndex(execution => new { execution.TenantId, execution.ReportId });
        builder.HasOne<Report>().WithMany().HasForeignKey(execution => execution.ReportId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(execution => execution.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(execution => execution.TriggeredByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
