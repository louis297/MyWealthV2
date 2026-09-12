using MyWealthV2.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MyWealthV2.Infrastructure.Data.Configurations;

public class CurrencyConfiguration : IEntityTypeConfiguration<Currency>
{
    public void Configure(EntityTypeBuilder<Currency> builder)
    {
        builder.ToTable("Currencies");
        builder.HasKey(currency => currency.Code);
        builder.Property(currency => currency.Code).HasColumnType("char(3)").IsFixedLength().IsRequired();
        builder.Property(currency => currency.Name).HasMaxLength(100).IsRequired();
        builder.Property(currency => currency.DecimalPlaces).HasConversion<byte>().IsRequired();
        builder.Property(currency => currency.IsEnabled).IsRequired();
    }
}
