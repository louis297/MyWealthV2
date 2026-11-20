using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.ValueObjects;

namespace MyWealthV2.Infrastructure.Data.Configurations;

/// <summary>Withdrawals §6.2: the payments the platform asks a bank to make (script 0043).</summary>
public class OutgoingPaymentConfiguration : IEntityTypeConfiguration<OutgoingPayment>
{
    public void Configure(EntityTypeBuilder<OutgoingPayment> builder)
    {
        builder.ToTable("OutgoingPayments");
        builder.Property(row => row.PublicId).IsRequired();
        builder.Property(row => row.Connection).HasMaxLength(BankConnectionName.MaxLength).IsRequired();
        builder.Property(row => row.Amount).HasColumnType("decimal(18,4)");
        builder.Property(row => row.Currency).HasColumnType("char(3)").IsFixedLength().IsRequired();
        builder.Property(row => row.Reference).HasMaxLength(Withdrawal.ReferenceMaxLength);
        builder.Property(row => row.BankPaymentId).HasMaxLength(OutgoingPayment.BankPaymentIdMaxLength);
        builder.Property(row => row.CreatedBy).HasMaxLength(450).IsRequired();
        builder.Property(row => row.RowVersion).IsRowVersion();
        builder.HasIndex(row => row.PublicId).IsUnique();
        builder.HasIndex(row => row.InstructionPublicId).IsUnique().HasDatabaseName("UX_OutgoingPayments_Instruction");
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
