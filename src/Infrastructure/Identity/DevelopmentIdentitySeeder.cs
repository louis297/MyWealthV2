using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MyWealthV2.Infrastructure.Identity;

public static class DevelopmentIdentitySeeder
{
    public const string SystemAdminEmail = "system-admin@localhost";
    public const string SystemAdminPassword = "Administrator1!";

    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<Data.ApplicationDbContext>();
        if (await db.DomainUsers.AnyAsync(user => user.Role == UserRole.SystemAdmin, cancellationToken))
        {
            return;
        }

        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var publicId = Guid.NewGuid();
        var identityUser = new ApplicationUser
        {
            UserName = publicId.ToString(),
            Email = SystemAdminEmail,
            EmailConfirmed = true
        };

        var result = await userManager.CreateAsync(identityUser, SystemAdminPassword);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                string.Join("; ", result.Errors.Select(error => error.Description)));
        }

        var person = User.Create(
            UserRole.SystemAdmin,
            name: "System Admin",
            email: SystemAdminEmail,
            identityUserId: identityUser.Id,
            publicId: publicId);

        db.DomainUsers.Add(person);
        await db.SaveChangesAsync(cancellationToken);
    }
}
