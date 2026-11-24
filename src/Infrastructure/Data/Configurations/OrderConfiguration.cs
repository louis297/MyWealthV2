using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Infrastructure.Data.Configurations;

/// <summary>Orders §6.1: a Tenant's orders (script 0051).</summary>
public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders");
        builder.Ignore(order => order.IsOpen);
        builder.Ignore(order => order.IsLive);
        builder.Ignore(order => order.IsUnsent);
        builder.Ignore(order => order.OpenQuantity);
        builder.Ignore(order => order.HasCancelRequest);
        builder.Property(order => order.PublicId).IsRequired();
        builder.Property(order => order.Quantity).HasColumnType("decimal(18,8)");
        builder.Property(order => order.Currency).HasColumnType("char(3)").IsFixedLength().IsRequired();
        builder.Property(order => order.ReferencePrice).HasColumnType("decimal(18,8)");
        builder.Property(order => order.ReferencePriceDate).HasColumnType("date").IsRequired();
        builder.Property(order => order.PriceLimit).HasColumnType("decimal(18,8)");
        builder.Property(order => order.Connection).HasMaxLength(50).IsRequired();
        builder.Property(order => order.FilledQuantity).HasColumnType("decimal(18,8)");
        builder.Property(order => order.PostedGross).HasColumnType("decimal(18,4)");
        builder.Property(order => order.FeeCharged).HasColumnType("decimal(18,4)");
        builder.Property(order => order.ReservedCash).HasColumnType("decimal(18,4)");
        builder.Property(order => order.ReservedQuantity).HasColumnType("decimal(18,8)");
        builder.Property(order => order.Memo).HasMaxLength(Order.MemoMaxLength);
        builder.Property(order => order.SettlementAccountNumber).HasMaxLength(ClientMoneyAccount.AccountNumberMaxLength);
        builder.Property(order => order.SafekeepingAccountNumber).HasMaxLength(SafekeepingAccount.AccountNumberMaxLength);
        builder.HasOne<SafekeepingAccount>()
            .WithMany()
            .HasForeignKey(order => order.SafekeepingAccountId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ClientMoneyAccount>()
            .WithMany()
            .HasForeignKey(order => order.ClientMoneyAccountId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Property(order => order.BrokerOrderId).HasMaxLength(Order.BrokerOrderIdMaxLength);
        builder.Property(order => order.CancelNote).HasMaxLength(Order.NoteMaxLength);
        builder.Property(order => order.CloseReason).HasMaxLength(Order.CloseReasonMaxLength);
        builder.Property(order => order.CreatedBy).HasMaxLength(450).IsRequired();
        builder.Property(order => order.RowVersion).IsRowVersion();
        builder.HasIndex(order => order.PublicId).IsUnique();
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(order => order.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(order => order.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Instrument>()
            .WithMany()
            .HasForeignKey(order => order.InstrumentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TradeFeeSchedule>()
            .WithMany()
            .HasForeignKey(order => order.TradeFeeScheduleId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(order => order.PlacedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(order => order.CancelRequestedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Rebalance>()
            .WithMany()
            .HasForeignKey(order => order.RebalanceId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(order => order.RebalanceId)
            .HasFilter("[RebalanceId] IS NOT NULL")
            .HasDatabaseName("IX_Orders_RebalanceId");
        builder.HasMany(order => order.Fills)
            .WithOne()
            .HasForeignKey(fill => fill.OrderId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_OrderFills_Orders_TenantId_OrderId");
        builder.Navigation(order => order.Fills)
            .HasField("_fills")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

/// <summary>Orders §6.1: the fills of an order, one per <c>Fill</c> report (script 0051).</summary>
public class OrderFillConfiguration : IEntityTypeConfiguration<OrderFill>
{
    public void Configure(EntityTypeBuilder<OrderFill> builder)
    {
        builder.ToTable("OrderFills");
        builder.HasKey(fill => fill.Id);
        builder.Property(fill => fill.Id).ValueGeneratedOnAdd();
        builder.Ignore(fill => fill.IsSettled);
        builder.Property(fill => fill.PublicId).IsRequired();
        builder.Property(fill => fill.Quantity).HasColumnType("decimal(18,8)");
        builder.Property(fill => fill.Price).HasColumnType("decimal(18,8)");
        builder.Property(fill => fill.TradeDate).HasColumnType("date").IsRequired();
        builder.Property(fill => fill.SettlementDate).HasColumnType("date").IsRequired();
        builder.Property(fill => fill.BrokerReference).HasMaxLength(OrderFill.BrokerReferenceMaxLength);
        builder.Property(fill => fill.BrokerSettlementId).HasMaxLength(ExecutionReport.IdMaxLength);
        builder.Property(fill => fill.BreakReason).HasMaxLength(ExecutionReport.ReasonMaxLength);
        builder.Property(fill => fill.BreakNote).HasMaxLength(Order.NoteMaxLength);
        builder.Ignore(fill => fill.IsBroken);
        builder.HasOne<TradeSettlement>()
            .WithMany()
            .HasForeignKey(fill => fill.TradeSettlementId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Property(fill => fill.NetCash).HasColumnType("decimal(18,4)");
        builder.Property(fill => fill.SettledDate).HasColumnType("date");
        builder.Property(fill => fill.RowVersion).IsRowVersion();
        builder.HasIndex(fill => fill.PublicId).IsUnique();
        builder.HasIndex(fill => fill.ExecutionReportId).IsUnique();
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(fill => fill.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ExecutionReport>()
            .WithMany()
            .HasForeignKey(fill => fill.ExecutionReportId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Transaction>()
            .WithMany()
            .HasForeignKey(fill => fill.TransactionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Orders §6.1: the execution reports of the broker feed, platform data (script 0051).</summary>
public class ExecutionReportConfiguration : IEntityTypeConfiguration<ExecutionReport>
{
    public void Configure(EntityTypeBuilder<ExecutionReport> builder)
    {
        builder.ToTable("ExecutionReports");
        builder.HasKey(report => report.Id);
        builder.Property(report => report.Id).ValueGeneratedOnAdd();
        builder.Property(report => report.PublicId).IsRequired();
        builder.Property(report => report.Connection).HasMaxLength(ExecutionReport.ConnectionMaxLength).IsRequired();
        builder.Property(report => report.OutsideId).HasMaxLength(ExecutionReport.IdMaxLength).IsRequired();
        builder.Property(report => report.ClientOrderId).HasMaxLength(ExecutionReport.IdMaxLength);
        builder.Property(report => report.BrokerOrderId).HasMaxLength(ExecutionReport.IdMaxLength);
        builder.Property(report => report.Quantity).HasColumnType("decimal(18,8)");
        builder.Property(report => report.Price).HasColumnType("decimal(18,8)");
        builder.Property(report => report.TradeDate).HasColumnType("date");
        builder.Property(report => report.SettlementDate).HasColumnType("date");
        builder.Property(report => report.ContractNote).HasMaxLength(ExecutionReport.ContractNoteMaxLength);
        builder.Property(report => report.FillOutsideId).HasMaxLength(ExecutionReport.IdMaxLength);
        builder.Property(report => report.SettledDate).HasColumnType("date");
        builder.Property(report => report.Reason).HasMaxLength(ExecutionReport.ReasonMaxLength);
        builder.Property(report => report.SettlementId).HasMaxLength(ExecutionReport.IdMaxLength);
        builder.Property(report => report.SettlementAccount).HasMaxLength(TradeSettlement.SettlementAccountMaxLength);
        builder.Property(report => report.Currency).HasColumnType("char(3)").IsFixedLength();
        builder.Property(report => report.NetAmount).HasColumnType("decimal(18,4)");
        builder.Property(report => report.RowVersion).IsRowVersion();
        builder.HasIndex(report => report.PublicId).IsUnique();
        builder.HasIndex(report => new { report.Connection, report.OutsideId }).IsUnique();
    }
}

/// <summary>Orders §6.1: the state of the execution report feed, one row per broker connection (script 0051).</summary>
public class BrokerFeedConfiguration : IEntityTypeConfiguration<BrokerFeed>
{
    public void Configure(EntityTypeBuilder<BrokerFeed> builder)
    {
        builder.ToTable("BrokerFeeds");
        builder.HasKey(feed => feed.Connection);
        builder.Property(feed => feed.Connection).HasMaxLength(BrokerFeed.ConnectionMaxLength);
        builder.Property(feed => feed.Cursor).HasMaxLength(BrokerFeed.CursorMaxLength);
        builder.Property(feed => feed.RowVersion).IsRowVersion();
    }
}
