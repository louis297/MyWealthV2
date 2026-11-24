using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.ValueObjects;

namespace MyWealthV2.Infrastructure.Data.Configurations;

/// <summary>In-kind transfers §6.1: the safekeeping accounts (script 0055).</summary>
public class SafekeepingAccountConfiguration : IEntityTypeConfiguration<SafekeepingAccount>
{
    public void Configure(EntityTypeBuilder<SafekeepingAccount> builder)
    {
        builder.ToTable("SafekeepingAccounts");
        builder.Property(row => row.PublicId).IsRequired();
        builder.Property(row => row.Currency).HasColumnType("char(3)").IsFixedLength().IsRequired();
        builder.Property(row => row.CustodianName).HasMaxLength(SafekeepingAccount.CustodianNameMaxLength).IsRequired();
        builder.Property(row => row.AccountName).HasMaxLength(SafekeepingAccount.AccountNameMaxLength).IsRequired();
        builder.Property(row => row.AccountNumber).HasMaxLength(SafekeepingAccount.AccountNumberMaxLength).IsRequired();
        builder.Property(row => row.CustodianConnection).HasMaxLength(BankConnectionName.MaxLength).IsRequired();
        builder.Property(row => row.CreatedBy).HasMaxLength(450).IsRequired();
        builder.Property(row => row.RowVersion).IsRowVersion();
        builder.HasIndex(row => row.PublicId).IsUnique();
        builder.HasIndex(row => new { row.TenantId, row.Currency })
            .IsUnique()
            .HasFilter("[IsActive] = 1")
            .HasDatabaseName("UX_SafekeepingAccounts_TenantId_Currency_Active");
        builder.HasIndex(row => row.AccountNumber)
            .IsUnique()
            .HasFilter("[IsActive] = 1")
            .HasDatabaseName("UX_SafekeepingAccounts_AccountNumber_Active");
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

/// <summary>In-kind transfers §6.1: one feed cursor per safekeeping account (script 0055).</summary>
public class CustodianFeedCursorConfiguration : IEntityTypeConfiguration<CustodianFeedCursor>
{
    public void Configure(EntityTypeBuilder<CustodianFeedCursor> builder)
    {
        builder.ToTable("CustodianFeedCursors");
        builder.HasKey(cursor => cursor.SafekeepingAccountId);
        builder.Property(cursor => cursor.SafekeepingAccountId).ValueGeneratedNever();
        builder.Property(cursor => cursor.Cursor).HasMaxLength(CustodianFeedCursor.CursorMaxLength);
        builder.Property(cursor => cursor.RowVersion).IsRowVersion();
        builder.HasOne<SafekeepingAccount>()
            .WithOne()
            .HasForeignKey<CustodianFeedCursor>(cursor => cursor.SafekeepingAccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>In-kind transfers §6.1: the custodian's positions of a safekeeping account (script 0055).</summary>
public class CustodianPositionConfiguration : IEntityTypeConfiguration<CustodianPosition>
{
    public void Configure(EntityTypeBuilder<CustodianPosition> builder)
    {
        builder.ToTable("CustodianPositions");
        builder.Property(position => position.Symbol).HasMaxLength(CustodianPosition.SymbolMaxLength).IsRequired();
        builder.Property(position => position.Quantity).HasColumnType("decimal(18,8)");
        builder.HasIndex(position => new { position.SafekeepingAccountId, position.Symbol }).IsUnique();
        builder.HasOne<SafekeepingAccount>()
            .WithMany()
            .HasForeignKey(position => position.SafekeepingAccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>In-kind transfers §6.1: the custodian's inbound records (script 0055).</summary>
public class CustodyMovementConfiguration : IEntityTypeConfiguration<CustodyMovement>
{
    public void Configure(EntityTypeBuilder<CustodyMovement> builder)
    {
        builder.ToTable("CustodyMovements");
        builder.Ignore(row => row.SignedQuantity);
        builder.Property(row => row.PublicId).IsRequired();
        builder.Property(row => row.Connection).HasMaxLength(BankConnectionName.MaxLength).IsRequired();
        builder.Property(row => row.OutsideId).HasMaxLength(CustodyMovement.OutsideIdMaxLength).IsRequired();
        builder.Property(row => row.Symbol).HasMaxLength(CustodyMovement.SymbolMaxLength).IsRequired();
        builder.Property(row => row.Currency).HasColumnType("char(3)").IsFixedLength().IsRequired();
        builder.Property(row => row.Quantity).HasColumnType("decimal(18,8)");
        builder.Property(row => row.InstructionId).HasMaxLength(CustodyMovement.ReferenceIdMaxLength);
        builder.Property(row => row.TradeReference).HasMaxLength(CustodyMovement.ReferenceIdMaxLength);
        builder.Property(row => row.CorporateActionReference).HasMaxLength(CustodyMovement.ReferenceIdMaxLength);
        builder.Property(row => row.CounterpartyName).HasMaxLength(CustodyMovement.CounterpartyNameMaxLength);
        builder.Property(row => row.CounterpartyAccount).HasMaxLength(CustodyMovement.CounterpartyAccountMaxLength);
        builder.Property(row => row.Reference).HasMaxLength(CustodyMovement.ReferenceMaxLength);
        builder.Property(row => row.LastActionNote).HasMaxLength(CustodyMovement.NoteMaxLength);
        builder.Property(row => row.CreatedBy).HasMaxLength(450).IsRequired();
        builder.Property(row => row.RowVersion).IsRowVersion();
        builder.HasIndex(row => row.PublicId).IsUnique();
        builder.HasIndex(row => new { row.Connection, row.OutsideId })
            .IsUnique()
            .HasDatabaseName("UX_CustodyMovements_Connection_OutsideId");
        builder.HasOne<SafekeepingAccount>()
            .WithMany()
            .HasForeignKey(row => row.SafekeepingAccountId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<InKindTransfer>()
            .WithMany()
            .HasForeignKey(row => row.InKindTransferId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OrderFill>()
            .WithMany()
            .HasForeignKey(row => row.OrderFillId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CorporateAction>()
            .WithMany()
            .HasForeignKey(row => row.CorporateActionId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>In-kind transfers §6.1: the transfer instructions (script 0055).</summary>
public class InKindTransferConfiguration : IEntityTypeConfiguration<InKindTransfer>
{
    public void Configure(EntityTypeBuilder<InKindTransfer> builder)
    {
        builder.ToTable("InKindTransfers");
        builder.Ignore(row => row.IsOpen);
        builder.Ignore(row => row.ReservedQuantity);
        builder.Property(row => row.PublicId).IsRequired();
        builder.Property(row => row.Quantity).HasColumnType("decimal(18,8)");
        builder.Property(row => row.Currency).HasColumnType("char(3)").IsFixedLength().IsRequired();
        builder.Property(row => row.Cost).HasColumnType("decimal(18,4)");
        builder.Property(row => row.AcquiredOn).HasColumnType("date");
        builder.Property(row => row.CounterpartyName).HasMaxLength(InKindTransfer.CounterpartyNameMaxLength).IsRequired();
        builder.Property(row => row.CounterpartyAccount).HasMaxLength(ClientMoneyAccount.AccountNumberMaxLength).IsRequired();
        builder.Property(row => row.CounterpartyHolderName).HasMaxLength(InKindTransfer.CounterpartyHolderNameMaxLength).IsRequired();
        builder.Property(row => row.Memo).HasMaxLength(InKindTransfer.MemoMaxLength);
        builder.Property(row => row.DecisionNote).HasMaxLength(InKindTransfer.NoteMaxLength);
        builder.Property(row => row.SetAsideNote).HasMaxLength(InKindTransfer.NoteMaxLength);
        builder.Property(row => row.CustodianTransferId).HasMaxLength(InKindTransfer.CustodianTransferIdMaxLength);
        builder.Property(row => row.CreatedBy).HasMaxLength(450).IsRequired();
        builder.Property(row => row.RowVersion).IsRowVersion();
        builder.HasIndex(row => row.PublicId).IsUnique();
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(row => row.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SafekeepingAccount>()
            .WithMany()
            .HasForeignKey(row => row.SafekeepingAccountId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>In-kind transfers §6.1: 0..1 in-kind transfer detail per Transaction (script 0055).</summary>
public class TransactionInKindTransferDetailConfiguration : IEntityTypeConfiguration<TransactionInKindTransferDetail>
{
    public void Configure(EntityTypeBuilder<TransactionInKindTransferDetail> builder)
    {
        builder.ToTable("TransactionInKindTransferDetails");
        builder.HasKey(detail => detail.TransactionId);
        builder.Property(detail => detail.TransactionId).ValueGeneratedNever();
        builder.Property(detail => detail.CounterpartyName).HasMaxLength(InKindTransfer.CounterpartyNameMaxLength).IsRequired();
        builder.Property(detail => detail.CounterpartyAccountMasked).HasMaxLength(ClientMoneyAccount.AccountNumberMaxLength).IsRequired();
        builder.Property(detail => detail.AcquiredOn).HasColumnType("date");
        builder.Property(detail => detail.SettlementDate).HasColumnType("date");
        builder.Property(detail => detail.CustodianReference).HasMaxLength(CustodyMovement.OutsideIdMaxLength).IsRequired();
    }
}
