using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Infrastructure.Data.Configurations;

/// <summary>Migration batches §6: a migration batch (script 0054).</summary>
public class MigrationBatchConfiguration : IEntityTypeConfiguration<MigrationBatch>
{
    public void Configure(EntityTypeBuilder<MigrationBatch> builder)
    {
        builder.ToTable("MigrationBatches");
        builder.Ignore(batch => batch.ControlTotals);
        builder.Property(batch => batch.PublicId).IsRequired();
        builder.Property(batch => batch.Name).HasMaxLength(MigrationBatch.TextMaxLength).IsRequired();
        builder.Property(batch => batch.PreviousProvider).HasMaxLength(MigrationBatch.TextMaxLength).IsRequired();
        builder.Property(batch => batch.AsOfDate).HasColumnType("date").IsRequired();
        builder.Property(batch => batch.ReturnNote).HasMaxLength(MigrationBatch.NoteMaxLength);
        builder.Property(batch => batch.CancelNote).HasMaxLength(MigrationBatch.NoteMaxLength);
        builder.Property(batch => batch.CreatedBy).HasMaxLength(450).IsRequired();
        builder.Property(batch => batch.RowVersion).IsRowVersion();
        builder.HasIndex(batch => batch.PublicId).IsUnique();
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(batch => batch.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(batch => batch.SubmittedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(batch => batch.ReturnedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(batch => batch.PostedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(batch => batch.CancelledByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(batch => batch.Entries)
            .WithOne()
            .HasForeignKey(entry => entry.MigrationBatchId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_MigrationBatchEntries_MigrationBatches_TenantId_MigrationBatchId");
        builder.Navigation(batch => batch.Entries)
            .HasField("_entries")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(batch => batch.ControlTotalRows)
            .WithOne()
            .HasForeignKey(row => row.MigrationBatchId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_MigrationBatchControlTotals_MigrationBatches_TenantId_MigrationBatchId");
        builder.Navigation(batch => batch.ControlTotalRows)
            .HasField("_controlTotalRows")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

/// <summary>Migration batches §6: one control total row of a batch (script 0054).</summary>
public class MigrationBatchControlTotalConfiguration : IEntityTypeConfiguration<MigrationBatchControlTotal>
{
    public void Configure(EntityTypeBuilder<MigrationBatchControlTotal> builder)
    {
        builder.ToTable("MigrationBatchControlTotals");
        builder.Property(row => row.Currency).HasColumnType("char(3)").IsFixedLength();
        builder.Property(row => row.Amount).HasColumnType("decimal(26,8)");
        builder.HasIndex(row => new { row.MigrationBatchId, row.Kind, row.Currency, row.InstrumentId }).IsUnique();
        builder.HasOne<Instrument>()
            .WithMany()
            .HasForeignKey(row => row.InstrumentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Migration batches §6: one entry of a batch (script 0054).</summary>
public class MigrationBatchEntryConfiguration : IEntityTypeConfiguration<MigrationBatchEntry>
{
    public void Configure(EntityTypeBuilder<MigrationBatchEntry> builder)
    {
        builder.ToTable("MigrationBatchEntries");
        builder.Property(entry => entry.ExternalReference).HasMaxLength(MigrationBatch.TextMaxLength);
        builder.Property(entry => entry.Cash).HasColumnName("CashAmount").HasColumnType("decimal(18,4)");
        builder.HasIndex(entry => new { entry.MigrationBatchId, entry.AccountId }).IsUnique();
        builder.HasIndex(entry => new { entry.MigrationBatchId, entry.Position }).IsUnique();
        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(entry => entry.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Transaction>()
            .WithMany()
            .HasForeignKey(entry => entry.OpeningTransactionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Transaction>()
            .WithMany()
            .HasForeignKey(entry => entry.OpeningHoldingsTransactionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(entry => entry.Holdings)
            .WithOne()
            .HasForeignKey(holding => holding.MigrationBatchEntryId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_MigrationBatchHoldings_MigrationBatchEntries_TenantId_MigrationBatchEntryId");
        builder.Navigation(entry => entry.Holdings)
            .HasField("_holdings")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

/// <summary>Migration batches §6: one holding of an entry (script 0054).</summary>
public class MigrationBatchHoldingConfiguration : IEntityTypeConfiguration<MigrationBatchHolding>
{
    public void Configure(EntityTypeBuilder<MigrationBatchHolding> builder)
    {
        builder.ToTable("MigrationBatchHoldings");
        builder.Property(holding => holding.Quantity).HasColumnType("decimal(18,8)");
        builder.Property(holding => holding.Cost).HasColumnType("decimal(18,4)");
        builder.HasIndex(holding => new { holding.MigrationBatchEntryId, holding.InstrumentId }).IsUnique();
        builder.HasOne<Instrument>()
            .WithMany()
            .HasForeignKey(holding => holding.InstrumentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Migration batches §6: 0..1 migration detail per Transaction (script 0054).</summary>
public class TransactionMigrationDetailConfiguration : IEntityTypeConfiguration<TransactionMigrationDetail>
{
    public void Configure(EntityTypeBuilder<TransactionMigrationDetail> builder)
    {
        builder.ToTable("TransactionMigrationDetails");
        builder.HasKey(detail => detail.TransactionId);
        builder.Property(detail => detail.TransactionId).ValueGeneratedNever();
        builder.Property(detail => detail.AsOfDate).HasColumnType("date").IsRequired();
        builder.Property(detail => detail.PreviousProvider).HasMaxLength(MigrationBatch.TextMaxLength).IsRequired();
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(detail => detail.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
