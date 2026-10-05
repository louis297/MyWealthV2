using MyWealthV2.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MyWealthV2.Infrastructure.Data.Configurations;

public class HoldingConfiguration : IEntityTypeConfiguration<Holding>
{
    public void Configure(EntityTypeBuilder<Holding> builder)
    {
        builder.ToTable("Holdings");
        builder.Property(holding => holding.PublicId).IsRequired();
        builder.Property(holding => holding.Quantity).HasColumnType("decimal(18,8)").IsRequired();
        builder.Property(holding => holding.CostAmount).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(holding => holding.CostCurrency).HasColumnType("char(3)").IsFixedLength().HasMaxLength(3).IsRequired();
        builder.Property(holding => holding.CreatedBy).HasMaxLength(450).IsRequired();
        builder.Property(holding => holding.RowVersion).IsRowVersion();
        builder.HasIndex(holding => holding.PublicId).IsUnique();
        builder.HasIndex(holding => new { holding.AccountId, holding.InstrumentId }).IsUnique();
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(holding => holding.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(holding => holding.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Instrument>()
            .WithMany()
            .HasForeignKey(holding => holding.InstrumentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Currency>()
            .WithMany()
            .HasForeignKey(holding => holding.CostCurrency)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
