using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Infrastructure.Data.Configurations;

/// <summary>Withdrawals §6.2: the withdrawal instructions (script 0043).</summary>
public class WithdrawalConfiguration : IEntityTypeConfiguration<Withdrawal>
{
    public void Configure(EntityTypeBuilder<Withdrawal> builder)
    {
        builder.ToTable("Withdrawals");
        builder.Ignore(row => row.IsOpen);
        builder.Property(row => row.PublicId).IsRequired();
        builder.Property(row => row.Amount).HasColumnType("decimal(18,4)");
        builder.Property(row => row.Currency).HasColumnType("char(3)").IsFixedLength().IsRequired();
        builder.Property(row => row.Reference).HasMaxLength(Withdrawal.ReferenceMaxLength).IsRequired();
        builder.Property(row => row.Memo).HasMaxLength(Withdrawal.MemoMaxLength);
        builder.Property(row => row.DecisionNote).HasMaxLength(Withdrawal.NoteMaxLength);
        builder.Property(row => row.ReturnReason).HasMaxLength(Withdrawal.ReturnReasonMaxLength);
        builder.Property(row => row.CreatedBy).HasMaxLength(450).IsRequired();
        builder.Property(row => row.RowVersion).IsRowVersion();
        builder.HasIndex(row => row.PublicId).IsUnique();
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(row => row.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Currency>()
            .WithMany()
            .HasForeignKey(row => row.Currency)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
