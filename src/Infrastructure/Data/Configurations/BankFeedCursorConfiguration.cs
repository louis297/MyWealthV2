using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Infrastructure.Data.Configurations;

/// <summary>Deposit matching §6.2: one cursor per client money account (script 0041).</summary>
public class BankFeedCursorConfiguration : IEntityTypeConfiguration<BankFeedCursor>
{
    public void Configure(EntityTypeBuilder<BankFeedCursor> builder)
    {
        builder.ToTable("BankFeedCursors");
        builder.HasKey(cursor => cursor.ClientMoneyAccountId);
        builder.Property(cursor => cursor.ClientMoneyAccountId).ValueGeneratedNever();
        builder.Property(cursor => cursor.Cursor).HasMaxLength(BankFeedCursor.CursorMaxLength);
        builder.Property(cursor => cursor.RowVersion).IsRowVersion();
        builder.HasOne<ClientMoneyAccount>()
            .WithOne()
            .HasForeignKey<BankFeedCursor>(cursor => cursor.ClientMoneyAccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
