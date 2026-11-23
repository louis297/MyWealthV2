using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Infrastructure.Data.Configurations;

/// <summary>Trade-settlement §6.1: the platform's record of each broker settlement (script 0052).</summary>
public class TradeSettlementConfiguration : IEntityTypeConfiguration<TradeSettlement>
{
    public void Configure(EntityTypeBuilder<TradeSettlement> builder)
    {
        builder.ToTable("TradeSettlements");
        builder.Property(row => row.PublicId).IsRequired();
        builder.Property(row => row.Connection).HasMaxLength(50).IsRequired();
        builder.Property(row => row.BrokerSettlementId).HasMaxLength(TradeSettlement.BrokerSettlementIdMaxLength).IsRequired();
        builder.Property(row => row.Currency).HasColumnType("char(3)").IsFixedLength().IsRequired();
        builder.Property(row => row.NetAmount).HasColumnType("decimal(18,4)");
        builder.Property(row => row.BrokerNetAmount).HasColumnType("decimal(18,4)");
        builder.Property(row => row.BrokerSettlementAccount).HasMaxLength(TradeSettlement.SettlementAccountMaxLength).IsRequired();
        builder.Property(row => row.FailureDetail).HasMaxLength(TradeSettlement.FailureDetailMaxLength);
        builder.Property(row => row.SetAsideNote).HasMaxLength(TradeSettlement.NoteMaxLength);
        builder.Property(row => row.CreatedBy).HasMaxLength(450).IsRequired();
        builder.Property(row => row.RowVersion).IsRowVersion();
        builder.HasIndex(row => row.PublicId).IsUnique();
        builder.HasIndex(row => new { row.Connection, row.BrokerSettlementId, row.ClientMoneyAccountId })
            .IsUnique()
            .HasDatabaseName("UX_TradeSettlements_Connection_BrokerSettlementId_ClientMoneyAccountId");
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(row => row.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ClientMoneyAccount>()
            .WithMany()
            .HasForeignKey(row => row.ClientMoneyAccountId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ExecutionReport>()
            .WithMany()
            .HasForeignKey(row => row.ExecutionReportId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
