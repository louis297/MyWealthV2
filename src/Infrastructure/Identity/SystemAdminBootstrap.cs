using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Infrastructure.Identity;

/// <summary>
/// Creates the first SystemAdmin from configuration when the database has none
/// (docs/features/identity-auth.md, amendment 2026-11-12).
/// </summary>
public static class SystemAdminBootstrap
{
    public const string EmailKey = "Bootstrap:SystemAdmin:Email";
    public const string PasswordKey = "Bootstrap:SystemAdmin:Password";
    public const string NameKey = "Bootstrap:SystemAdmin:Name";
    public const string DefaultName = "System Admin";

    public static async Task RunAsync(
        IServiceProvider services,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(SystemAdminBootstrap));
        var email = configuration[EmailKey];
        var password = configuration[PasswordKey];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning(
                "The database has no SystemAdmin. Set {EmailKey} and {PasswordKey} to create one.",
                EmailKey,
                PasswordKey);
            return;
        }

        var name = configuration[NameKey];
        if (string.IsNullOrWhiteSpace(name))
        {
            name = DefaultName;
        }

        var db = services.GetRequiredService<ApplicationDbContext>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        await DevelopmentIdentitySeeder.EnsurePersonAsync(
            db,
            userManager,
            UserRole.SystemAdmin,
            name,
            email,
            password,
            tenantId: null,
            cancellationToken);

        logger.LogInformation("Bootstrap SystemAdmin {Email} created.", email);
    }
}
