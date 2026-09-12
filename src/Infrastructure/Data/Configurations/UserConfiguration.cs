using MyWealthV2.Domain.Entities;
using MyWealthV2.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MyWealthV2.Infrastructure.Data.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
        builder.Property(user => user.Name).HasMaxLength(200).IsRequired();
        builder.Property(user => user.Email).HasMaxLength(256).IsRequired();
        builder.Property(user => user.IdentityUserId).HasMaxLength(450).IsRequired();
        builder.Property(user => user.PublicId).IsRequired();
        builder.Property(user => user.RowVersion).IsRowVersion();

        builder.HasIndex(user => user.PublicId).IsUnique();
        builder.HasIndex(user => user.IdentityUserId).IsUnique();

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(user => user.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(user => user.AdviserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(user => user.IdentityUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
