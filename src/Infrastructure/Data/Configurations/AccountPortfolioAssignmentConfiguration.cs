using MyWealthV2.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MyWealthV2.Infrastructure.Data.Configurations;

public class AccountPortfolioAssignmentConfiguration : IEntityTypeConfiguration<AccountPortfolioAssignment>
{
    public void Configure(EntityTypeBuilder<AccountPortfolioAssignment> builder)
    {
        builder.ToTable("AccountPortfolioAssignments");
        builder.Property(assignment => assignment.PublicId).IsRequired();
        builder.Property(assignment => assignment.AssignedBy).HasMaxLength(450).IsRequired();
        builder.Property(assignment => assignment.LastRebalanceMessage).HasMaxLength(500);
        builder.Property(assignment => assignment.CreatedBy).HasMaxLength(450).IsRequired();
        builder.Property(assignment => assignment.RowVersion).IsRowVersion();
        builder.HasIndex(assignment => assignment.PublicId).IsUnique();
        builder.HasIndex(assignment => assignment.AccountId)
            .IsUnique()
            .HasFilter("[Status] = 0")
            .HasDatabaseName("IX_AccountPortfolioAssignments_AccountId_Active");
        builder.HasIndex(assignment => assignment.TenantId);
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(assignment => assignment.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(assignment => assignment.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Portfolio>()
            .WithMany()
            .HasForeignKey(assignment => assignment.PortfolioId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
