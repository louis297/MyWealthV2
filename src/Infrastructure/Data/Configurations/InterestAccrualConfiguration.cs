using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Infrastructure.Data.Configurations;

/// <summary>Interest run §6.2: one daily accrual row per account and UTC day (script 0048).</summary>
public class InterestAccrualConfiguration : IEntityTypeConfiguration<InterestAccrual>
{
    public void Configure(EntityTypeBuilder<InterestAccrual> builder)
    {
        builder.ToTable("InterestAccruals");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedOnAdd();
        builder.Property(row => row.Date).HasColumnName("AccrualDate").HasColumnType("date").IsRequired();
        builder.Property(row => row.Balance).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(row => row.EffectiveRatePercent).HasColumnType("decimal(7,4)").IsRequired();
        builder.Property(row => row.Amount).HasColumnType("decimal(18,8)").IsRequired();
        builder.HasIndex(row => new { row.AccountId, row.Date }).IsUnique();
        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(row => row.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<InterestRateSchedule>()
            .WithMany()
            .HasForeignKey(row => row.InterestRateScheduleId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Transaction>()
            .WithMany()
            .HasForeignKey(row => row.PostedTransactionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(row => row.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
