using MyWealthV2.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MyWealthV2.Infrastructure.Data.Configurations;

public class InstrumentConfiguration : IEntityTypeConfiguration<Instrument>
{
    public void Configure(EntityTypeBuilder<Instrument> builder)
    {
        builder.ToTable("Instruments");
        builder.Property(instrument => instrument.Symbol).HasMaxLength(32).IsRequired();
        builder.Property(instrument => instrument.Name).HasMaxLength(200).IsRequired();
        builder.Property(instrument => instrument.PublicId).IsRequired();
        builder.Property(instrument => instrument.QuoteCurrency).HasColumnType("char(3)").IsFixedLength().IsRequired();
        builder.Property(instrument => instrument.IsActive).IsRequired();
        builder.Property(instrument => instrument.CreatedBy).HasMaxLength(450).IsRequired();
        builder.Property(instrument => instrument.RowVersion).IsRowVersion();
        // The tax attributes are mapped with script 0061 (instrument-tax-attributes §6).
        builder.Ignore(instrument => instrument.IssuerCountry);
        builder.Ignore(instrument => instrument.FifTreatments);
        builder.HasIndex(instrument => instrument.PublicId).IsUnique();
        builder.HasIndex(instrument => new { instrument.TenantId, instrument.Symbol }).IsUnique();
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(instrument => instrument.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Currency>()
            .WithMany()
            .HasForeignKey(instrument => instrument.QuoteCurrency)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
