using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Infrastructure.Data.Configurations;

/// <summary>Corporate actions §6.1: the announcements of the corporate action feed, platform data (script 0050).</summary>
public class CorporateActionAnnouncementConfiguration : IEntityTypeConfiguration<CorporateActionAnnouncement>
{
    public void Configure(EntityTypeBuilder<CorporateActionAnnouncement> builder)
    {
        builder.ToTable("CorporateActionAnnouncements");
        builder.Property(row => row.PublicId).IsRequired();
        builder.Property(row => row.Connection).HasMaxLength(CorporateActionAnnouncement.ConnectionMaxLength).IsRequired();
        builder.Property(row => row.OutsideId).HasMaxLength(CorporateActionAnnouncement.IdMaxLength).IsRequired();
        builder.Property(row => row.PartyActionId).HasMaxLength(CorporateActionAnnouncement.IdMaxLength).IsRequired();
        builder.Property(row => row.Symbol).HasMaxLength(CorporateActionAnnouncement.SymbolMaxLength).IsRequired();
        builder.Property(row => row.Currency).HasColumnType("char(3)").IsFixedLength().IsRequired();
        builder.Property(row => row.ExDate).HasColumnType("date").IsRequired();
        builder.Property(row => row.RecordDate).HasColumnType("date");
        builder.Property(row => row.PaymentDate).HasColumnType("date");
        builder.Property(row => row.RowVersion).IsRowVersion();
        builder.HasIndex(row => row.PublicId).IsUnique();
        builder.HasIndex(row => new { row.Connection, row.OutsideId }).IsUnique();
    }
}
