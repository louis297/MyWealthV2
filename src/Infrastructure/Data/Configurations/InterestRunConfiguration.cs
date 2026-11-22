using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Infrastructure.Data.Configurations;

/// <summary>Interest run §6.2: stored interest runs (script 0048).</summary>
public class InterestRunConfiguration : IEntityTypeConfiguration<InterestRun>
{
    public void Configure(EntityTypeBuilder<InterestRun> builder)
    {
        builder.ToTable("InterestRuns");
        builder.Property(run => run.PublicId).IsRequired();
        builder.Property(run => run.AsOfDate).HasColumnType("date").IsRequired();
        builder.Property(run => run.CreatedBy).HasMaxLength(450).IsRequired();
        builder.Ignore(run => run.PostingDate);
        builder.HasIndex(run => run.PublicId).IsUnique();
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(run => run.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(run => run.RanByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(run => run.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
