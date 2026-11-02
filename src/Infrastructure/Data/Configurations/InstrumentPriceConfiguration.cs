using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Infrastructure.Data.Entities;

namespace MyWealthV2.Infrastructure.Data.Configurations;

public class InstrumentPriceConfiguration : IEntityTypeConfiguration<InstrumentPrice>
{
    public void Configure(EntityTypeBuilder<InstrumentPrice> builder)
    {
        builder.ToTable("InstrumentPrices");
        builder.HasKey(price => new { price.InstrumentId, price.AsOfDate });
        builder.Property(price => price.Price).HasPrecision(18, 8).IsRequired();
        builder.Property(price => price.AsOfDate).HasColumnType("date").IsRequired();
        builder.Property(price => price.Source).HasMaxLength(50).IsRequired();
        builder.Property(price => price.FetchedAt).IsRequired();
        builder.HasIndex(price => price.FetchedAt).HasDatabaseName("IX_InstrumentPrices_FetchedAt");
        builder.HasOne<Instrument>()
            .WithMany()
            .HasForeignKey(price => price.InstrumentId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_InstrumentPrices_Instruments_InstrumentId");
    }
}
