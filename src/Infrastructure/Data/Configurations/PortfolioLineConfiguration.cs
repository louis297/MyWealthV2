using MyWealthV2.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MyWealthV2.Infrastructure.Data.Configurations;

public class PortfolioLineConfiguration : IEntityTypeConfiguration<PortfolioLine>
{
    public void Configure(EntityTypeBuilder<PortfolioLine> builder)
    {
        builder.ToTable("PortfolioLines");
        builder.Property(line => line.TargetWeight).HasColumnType("decimal(9,2)").IsRequired();
        builder.HasIndex(line => new { line.PortfolioId, line.InstrumentId }).IsUnique();
        builder.HasOne<Instrument>()
            .WithMany()
            .HasForeignKey(line => line.InstrumentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(line => line.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
