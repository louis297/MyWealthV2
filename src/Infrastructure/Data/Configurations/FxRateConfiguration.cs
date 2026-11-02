using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Infrastructure.Data.Entities;

namespace MyWealthV2.Infrastructure.Data.Configurations;

public class FxRateConfiguration : IEntityTypeConfiguration<FxRate>
{
    public void Configure(EntityTypeBuilder<FxRate> builder)
    {
        builder.ToTable("FxRates");
        builder.HasKey(rate => new { rate.BaseCurrency, rate.QuoteCurrency, rate.AsOfDate });
        builder.Property(rate => rate.BaseCurrency).HasColumnType("char(3)").IsFixedLength().IsRequired();
        builder.Property(rate => rate.QuoteCurrency).HasColumnType("char(3)").IsFixedLength().IsRequired();
        builder.Property(rate => rate.Rate).HasPrecision(18, 8).IsRequired();
        builder.Property(rate => rate.AsOfDate).HasColumnType("date").IsRequired();
        builder.Property(rate => rate.Source).HasMaxLength(50).IsRequired();
        builder.Property(rate => rate.FetchedAt).IsRequired();
        builder.HasIndex(rate => rate.FetchedAt).HasDatabaseName("IX_FxRates_FetchedAt");
        builder.HasOne<Currency>()
            .WithMany()
            .HasForeignKey(rate => rate.BaseCurrency)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_FxRates_Currencies_BaseCurrency");
        builder.HasOne<Currency>()
            .WithMany()
            .HasForeignKey(rate => rate.QuoteCurrency)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_FxRates_Currencies_QuoteCurrency");
    }
}
