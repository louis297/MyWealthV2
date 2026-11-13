using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Infrastructure.Data.Configurations;

public class TransactionFxDetailConfiguration : IEntityTypeConfiguration<TransactionFxDetail>
{
    public void Configure(EntityTypeBuilder<TransactionFxDetail> builder)
    {
        builder.ToTable("TransactionFxDetails");
        builder.HasKey(detail => detail.TransactionId);
        builder.Property(detail => detail.TransactionId).ValueGeneratedNever();
        builder.Property(detail => detail.BaseCurrency).HasColumnType("char(3)").IsFixedLength().HasMaxLength(3).IsRequired();
        builder.Property(detail => detail.QuoteCurrency).HasColumnType("char(3)").IsFixedLength().HasMaxLength(3).IsRequired();
        builder.Property(detail => detail.Rate).HasPrecision(18, 8).IsRequired();
        builder.Property(detail => detail.RateAsOfDate).HasColumnType("date").IsRequired();
        builder.Property(detail => detail.RateSource).HasMaxLength(50).IsRequired();
        builder.HasOne<Currency>()
            .WithMany()
            .HasForeignKey(detail => detail.BaseCurrency)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_TransactionFxDetails_Currencies_BaseCurrency");
        builder.HasOne<Currency>()
            .WithMany()
            .HasForeignKey(detail => detail.QuoteCurrency)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_TransactionFxDetails_Currencies_QuoteCurrency");
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(detail => detail.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
