using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Infrastructure.Data.Configurations;

/// <summary>Bank-reconciliation §6.1: stored runs and their items (script 0045).</summary>
public class BankReconciliationConfiguration : IEntityTypeConfiguration<BankReconciliation>
{
    public void Configure(EntityTypeBuilder<BankReconciliation> builder)
    {
        builder.ToTable("BankReconciliations");
        builder.Property(row => row.PublicId).IsRequired();
        builder.Property(row => row.Currency).HasColumnType("char(3)").IsFixedLength().IsRequired();
        builder.Property(row => row.CreatedBy).HasMaxLength(450).IsRequired();
        foreach (var amount in new[]
                 {
                     nameof(BankReconciliation.BankBalance), nameof(BankReconciliation.OpeningBalance),
                     nameof(BankReconciliation.StatementLinesTotal), nameof(BankReconciliation.CustodialCash),
                     nameof(BankReconciliation.Difference), nameof(BankReconciliation.BreakTotal),
                     nameof(BankReconciliation.TimingTotal), nameof(BankReconciliation.KnownTotal),
                     nameof(BankReconciliation.ReservedCash), nameof(BankReconciliation.PaymentsInFlightAmount),
                     nameof(BankReconciliation.OpenSettlementsAmount)
                 })
        {
            builder.Property(amount).HasColumnType("decimal(18,4)");
        }

        builder.HasIndex(row => row.PublicId).IsUnique();
        builder.HasMany(row => row.Items)
            .WithOne()
            .HasForeignKey(item => item.ReconciliationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(row => row.Items).HasField("_items").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(row => row.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ClientMoneyAccount>()
            .WithMany()
            .HasForeignKey(row => row.ClientMoneyAccountId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Currency>()
            .WithMany()
            .HasForeignKey(row => row.Currency)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class BankReconciliationItemConfiguration : IEntityTypeConfiguration<BankReconciliationItem>
{
    public void Configure(EntityTypeBuilder<BankReconciliationItem> builder)
    {
        builder.ToTable("BankReconciliationItems");
        builder.Property(row => row.Amount).HasColumnType("decimal(18,4)");
        builder.Property(row => row.Note).HasMaxLength(BankStatementLine.NoteMaxLength);
        builder.HasIndex(row => new { row.ReconciliationId, row.Position }).IsUnique();
        builder.HasOne<ClientMoneyAccount>()
            .WithMany()
            .HasForeignKey(row => row.OtherClientMoneyAccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
