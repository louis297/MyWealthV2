using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Rebalances;

namespace MyWealthV2.Infrastructure.Data.Configurations;

/// <summary>Rebalances §6: a rebalance of one assigned account (script 0053).</summary>
public class RebalanceConfiguration : IEntityTypeConfiguration<Rebalance>
{
    public void Configure(EntityTypeBuilder<Rebalance> builder)
    {
        builder.ToTable("Rebalances");
        builder.Ignore(rebalance => rebalance.IsOpen);
        builder.Ignore(rebalance => rebalance.IsInProgress);
        builder.Ignore(rebalance => rebalance.IsConfirmed);
        builder.Ignore(rebalance => rebalance.HasCancelRequest);
        builder.Property(rebalance => rebalance.PublicId).IsRequired();
        builder.Property(rebalance => rebalance.BaseCurrency).HasColumnType("char(3)").IsFixedLength().IsRequired();
        builder.Property(rebalance => rebalance.TotalValue).HasColumnType("decimal(18,4)");
        builder.Property(rebalance => rebalance.Cash).HasColumnType("decimal(18,4)");
        builder.Property(rebalance => rebalance.MinimumTrade).HasColumnType("decimal(18,4)");
        builder.Property(rebalance => rebalance.CashResidualPercent).HasColumnType("decimal(5,2)");
        builder.Property(rebalance => rebalance.Connection).HasMaxLength(50);
        builder.Property(rebalance => rebalance.FailureCode).HasMaxLength(RebalanceRules.FailureCodeMaxLength);
        builder.Property(rebalance => rebalance.FailureMessage).HasMaxLength(RebalanceRules.FailureMessageMaxLength);
        builder.Property(rebalance => rebalance.CancelNote).HasMaxLength(RebalanceRules.NoteMaxLength);
        builder.Property(rebalance => rebalance.CreatedBy).HasMaxLength(450).IsRequired();
        builder.Property(rebalance => rebalance.RowVersion).IsRowVersion();
        builder.HasIndex(rebalance => rebalance.PublicId).IsUnique();
        builder.HasIndex(rebalance => rebalance.AccountId)
            .IsUnique()
            .HasFilter("[Status] IN (0, 1, 2)")
            .HasDatabaseName("UX_Rebalances_AccountId_Open");
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(rebalance => rebalance.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(rebalance => rebalance.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Portfolio>()
            .WithMany()
            .HasForeignKey(rebalance => rebalance.PortfolioId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AccountPortfolioAssignment>()
            .WithMany()
            .HasForeignKey(rebalance => rebalance.AssignmentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(rebalance => rebalance.ProposedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(rebalance => rebalance.ConfirmedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(rebalance => rebalance.CancelRequestedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(rebalance => rebalance.Targets)
            .WithOne()
            .HasForeignKey(target => target.RebalanceId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_RebalanceTargets_Rebalances_TenantId_RebalanceId");
        builder.Navigation(rebalance => rebalance.Targets)
            .HasField("_targets")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(rebalance => rebalance.Lines)
            .WithOne()
            .HasForeignKey(line => line.RebalanceId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_RebalanceLines_Rebalances_TenantId_RebalanceId");
        builder.Navigation(rebalance => rebalance.Lines)
            .HasField("_lines")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

/// <summary>Rebalances §6: a rebalance's own copy of the portfolio's lines (script 0053).</summary>
public class RebalanceTargetConfiguration : IEntityTypeConfiguration<RebalanceTarget>
{
    public void Configure(EntityTypeBuilder<RebalanceTarget> builder)
    {
        builder.ToTable("RebalanceTargets");
        builder.Property(target => target.TargetWeight).HasColumnType("decimal(5,2)");
        builder.HasIndex(target => new { target.RebalanceId, target.InstrumentId }).IsUnique();
        builder.HasOne<Instrument>()
            .WithMany()
            .HasForeignKey(target => target.InstrumentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Rebalances §6: the lines of a rebalance proposal (script 0053).</summary>
public class RebalanceLineConfiguration : IEntityTypeConfiguration<RebalanceLine>
{
    public void Configure(EntityTypeBuilder<RebalanceLine> builder)
    {
        builder.ToTable("RebalanceLines");
        builder.Property(line => line.Quantity).HasColumnType("decimal(18,8)");
        builder.Property(line => line.Weight).HasColumnType("decimal(5,2)");
        builder.Property(line => line.TargetWeight).HasColumnType("decimal(5,2)");
        builder.Property(line => line.TradeQuantity).HasColumnType("decimal(18,8)");
        builder.Property(line => line.ReferencePrice).HasColumnType("decimal(18,8)");
        builder.Property(line => line.ReferencePriceDate).HasColumnType("date");
        builder.Property(line => line.EstimatedGross).HasColumnType("decimal(18,4)");
        builder.Property(line => line.EstimatedFee).HasColumnType("decimal(18,4)");
        builder.Property(line => line.Currency).HasColumnType("char(3)").IsFixedLength().IsRequired();
        builder.HasIndex(line => new { line.RebalanceId, line.InstrumentId }).IsUnique();
        builder.HasOne<Instrument>()
            .WithMany()
            .HasForeignKey(line => line.InstrumentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
