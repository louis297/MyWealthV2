using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Infrastructure.Data.Configurations;

/// <summary>Simulated instruments §6.1: one row per market data connection and feed kind (script 0049).</summary>
public class MarketDataFeedConfiguration : IEntityTypeConfiguration<MarketDataFeed>
{
    public void Configure(EntityTypeBuilder<MarketDataFeed> builder)
    {
        builder.ToTable("MarketDataFeeds");
        builder.HasKey(feed => new { feed.Connection, feed.Kind });
        builder.Property(feed => feed.Connection).HasMaxLength(MarketDataFeed.ConnectionMaxLength);
        builder.Property(feed => feed.Cursor).HasMaxLength(MarketDataFeed.CursorMaxLength);
        builder.Property(feed => feed.RowVersion).IsRowVersion();
    }
}
