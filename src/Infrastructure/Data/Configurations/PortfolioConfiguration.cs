using MyWealthV2.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MyWealthV2.Infrastructure.Data.Configurations;

public class PortfolioConfiguration : IEntityTypeConfiguration<Portfolio>
{
    public void Configure(EntityTypeBuilder<Portfolio> builder)
    {
        builder.ToTable("Portfolios");
        builder.Property(portfolio => portfolio.PublicId).IsRequired();
        builder.Property(portfolio => portfolio.Name).HasMaxLength(200).IsRequired();
        builder.Property(portfolio => portfolio.BaseCurrency).HasColumnType("char(3)").IsFixedLength().IsRequired();
        builder.Property(portfolio => portfolio.DriftAbsolute).HasColumnType("decimal(9,4)").IsRequired();
        builder.Property(portfolio => portfolio.DriftRelative).HasColumnType("decimal(9,4)").IsRequired();
        builder.Property(portfolio => portfolio.MinimumTrade).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(portfolio => portfolio.CashResidualPercent).HasColumnType("decimal(9,4)").IsRequired();
        builder.Property(portfolio => portfolio.CreatedBy).HasMaxLength(450).IsRequired();
        builder.Property(portfolio => portfolio.RowVersion).IsRowVersion();
        builder.HasIndex(portfolio => portfolio.PublicId).IsUnique();
        builder.HasIndex(portfolio => new { portfolio.TenantId, portfolio.IsActive });
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(portfolio => portfolio.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Currency>()
            .WithMany()
            .HasForeignKey(portfolio => portfolio.BaseCurrency)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(portfolio => portfolio.Lines)
            .WithOne()
            .HasForeignKey(line => line.PortfolioId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(portfolio => portfolio.Lines)
            .HasField("_lines")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
