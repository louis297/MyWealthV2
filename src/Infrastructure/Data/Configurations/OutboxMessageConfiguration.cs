using MyWealthV2.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MyWealthV2.Infrastructure.Data.Configurations;

public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");
        builder.Property(message => message.Id).ValueGeneratedNever();
        builder.Property(message => message.Type).HasMaxLength(200).IsRequired();
        builder.Property(message => message.Payload).IsRequired();
        builder.Property(message => message.OccurredAt).IsRequired();
        builder.Property(message => message.Status).HasMaxLength(20).IsRequired();
        builder.Property(message => message.Error).HasMaxLength(2000);
        builder.Property(message => message.RetryCount).IsRequired();
        builder.Property(message => message.Created).IsRequired();
        builder.Property(message => message.CreatedBy).HasMaxLength(450);
        builder.Property(message => message.LastModifiedBy).HasMaxLength(450);
        builder.Property(message => message.RowVersion).IsRowVersion();
        builder.HasIndex(message => new { message.Status, message.OccurredAt });
    }
}
