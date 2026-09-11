using MyWealthV2.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MyWealthV2.Infrastructure.Data.Configurations;

public class UserTokenConfiguration : IEntityTypeConfiguration<UserToken>
{
    public void Configure(EntityTypeBuilder<UserToken> builder)
    {
        builder.ToTable("UserTokens");
        builder.Property(token => token.Email).HasMaxLength(256).IsRequired();
        builder.Property(token => token.IdentityUserId).HasMaxLength(450);
        builder.Property(token => token.TokenHash).HasMaxLength(128).IsRequired();
        builder.Property(token => token.CreatedBy).HasMaxLength(450).IsRequired();
        builder.HasIndex(token => token.TokenHash).IsUnique();
    }
}
