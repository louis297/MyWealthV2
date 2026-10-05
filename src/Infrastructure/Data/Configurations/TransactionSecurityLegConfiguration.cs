using MyWealthV2.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MyWealthV2.Infrastructure.Data.Configurations;

public class TransactionSecurityLegConfiguration : IEntityTypeConfiguration<TransactionSecurityLeg>
{
    public void Configure(EntityTypeBuilder<TransactionSecurityLeg> builder)
    {
        builder.ToTable("TransactionSecurityLegs");
        builder.Property(leg => leg.Quantity).HasColumnType("decimal(18,8)").IsRequired();
        builder.Property(leg => leg.CostAmount).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(leg => leg.CostCurrency).HasColumnType("char(3)").IsFixedLength().HasMaxLength(3).IsRequired();
        builder.Property(leg => leg.CreatedBy).HasMaxLength(450).IsRequired();
        builder.Property(leg => leg.RowVersion).IsRowVersion();
        builder.HasIndex(leg => new { leg.TransactionId, leg.InstrumentId }).IsUnique();
        builder.HasOne<Transaction>()
            .WithMany()
            .HasForeignKey(leg => leg.TransactionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Instrument>()
            .WithMany()
            .HasForeignKey(leg => leg.InstrumentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Currency>()
            .WithMany()
            .HasForeignKey(leg => leg.CostCurrency)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
