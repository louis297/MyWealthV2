using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.ValueObjects;

namespace MyWealthV2.Infrastructure.Data.Configurations;

/// <summary>Deposit matching §6.2: the bank's inbound records (script 0041).</summary>
public class BankStatementLineConfiguration : IEntityTypeConfiguration<BankStatementLine>
{
    public void Configure(EntityTypeBuilder<BankStatementLine> builder)
    {
        builder.ToTable("BankStatementLines");
        builder.Property(line => line.PublicId).IsRequired();
        builder.Property(line => line.Connection).HasMaxLength(BankConnectionName.MaxLength).IsRequired();
        builder.Property(line => line.OutsideId).HasMaxLength(BankStatementLine.OutsideIdMaxLength).IsRequired();
        builder.Property(line => line.Amount).HasColumnType("decimal(18,4)");
        builder.Property(line => line.Currency).HasColumnType("char(3)").IsFixedLength().IsRequired();
        builder.Property(line => line.PayerName).HasMaxLength(BankStatementLine.FreeTextMaxLength);
        builder.Property(line => line.PayerAccountMasked).HasMaxLength(BankStatementLine.PayerAccountMaxLength);
        builder.Property(line => line.ReferenceText).HasMaxLength(BankStatementLine.FreeTextMaxLength);
        builder.Property(line => line.LastActionNote).HasMaxLength(BankStatementLine.NoteMaxLength);
        builder.Property(line => line.CreatedBy).HasMaxLength(450).IsRequired();
        builder.Property(line => line.RowVersion).IsRowVersion();
        builder.HasIndex(line => line.PublicId).IsUnique();
        builder.HasIndex(line => new { line.Connection, line.OutsideId })
            .IsUnique()
            .HasDatabaseName("UX_BankStatementLines_Connection_OutsideId");
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(line => line.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ClientMoneyAccount>()
            .WithMany()
            .HasForeignKey(line => line.ClientMoneyAccountId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Currency>()
            .WithMany()
            .HasForeignKey(line => line.Currency)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
