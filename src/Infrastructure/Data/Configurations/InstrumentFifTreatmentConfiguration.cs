using MyWealthV2.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MyWealthV2.Infrastructure.Data.Configurations;

/// <summary>Instrument-tax-attributes §6.1: script 0061 owns the table and its composite tenant FK.</summary>
public class InstrumentFifTreatmentConfiguration : IEntityTypeConfiguration<InstrumentFifTreatment>
{
    public void Configure(EntityTypeBuilder<InstrumentFifTreatment> builder)
    {
        builder.ToTable("InstrumentFifTreatments");
        builder.Property(row => row.TaxYear).HasColumnType("smallint").HasConversion<short>().IsRequired();
        builder.Property(row => row.Treatment).HasColumnType("tinyint").HasConversion<byte>().IsRequired();
        builder.Property(row => row.Note).HasMaxLength(500);
        builder.Property(row => row.CreatedBy).HasMaxLength(450).IsRequired();
        builder.HasIndex(row => new { row.InstrumentId, row.TaxYear }).IsUnique();
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(row => row.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
