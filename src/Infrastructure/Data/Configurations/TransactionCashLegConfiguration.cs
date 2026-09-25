using MyWealthV2.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MyWealthV2.Infrastructure.Data.Configurations;

public class TransactionCashLegConfiguration : IEntityTypeConfiguration<TransactionCashLeg>
{
    public void Configure(EntityTypeBuilder<TransactionCashLeg> builder)
    {
        builder.ToTable("TransactionCashLegs");
        builder.Property(leg => leg.Amount).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(leg => leg.Currency).HasColumnType("char(3)").IsFixedLength().HasMaxLength(3).IsRequired();
        builder.Property(leg => leg.CreatedBy).HasMaxLength(450).IsRequired();
        builder.Property(leg => leg.RowVersion).IsRowVersion();
        builder.HasIndex(leg => leg.TransactionId).IsUnique();
        builder.HasOne<Currency>()
            .WithMany()
            .HasForeignKey(leg => leg.Currency)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
