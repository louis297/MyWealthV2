using System.Net.Mail;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Infrastructure.Identity;

/// <summary>
/// Creates the first platform-level person of one role from configuration when the database has none
/// (identity-auth amendment 2026-11-12, A25–A31). Used by <see cref="SystemAdminBootstrap"/> and
/// <see cref="SimulatorOperatorBootstrap"/>.
/// </summary>
internal sealed record PlatformPersonBootstrap(
    UserRole Role,
    string Label,
    string EmailKey,
    string PasswordKey,
    string NameKey,
    string DefaultName)
{
    public async Task RunAsync(
        IServiceProvider services,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();

        // Any status counts: an existing person is never changed, re-enabled or reset.
        if (await db.DomainUsers.IgnoreQueryFilters()
                .AnyAsync(user => user.Role == Role, cancellationToken))
        {
            return;
        }

        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(PlatformPersonBootstrap));
        var email = configuration[EmailKey];
        var password = configuration[PasswordKey];
        var hasEmail = !string.IsNullOrWhiteSpace(email);
        var hasPassword = !string.IsNullOrWhiteSpace(password);
        if (!hasEmail && !hasPassword)
        {
            logger.LogWarning(
                "The database has no {Role}. Set {EmailKey} and {PasswordKey} to create one.",
                Label,
                EmailKey,
                PasswordKey);
            return;
        }

        if (!hasEmail || !hasPassword)
        {
            var missing = hasEmail ? PasswordKey : EmailKey;
            throw new InvalidOperationException(
                $"Bootstrap {Label}: {missing} is not set. Set both {EmailKey} and {PasswordKey}, or neither.");
        }

        if (!MailAddress.TryCreate(email, out var address) || address.Address != email)
        {
            throw new InvalidOperationException($"Bootstrap {Label}: {EmailKey} is not a valid email address.");
        }

        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var failedRules = await FailedPasswordRulesAsync(userManager, email!, password!);
        if (failedRules.Count > 0)
        {
            // Rule codes only: the value itself must never reach an exception or a log.
            throw new InvalidOperationException(
                $"Bootstrap {Label}: {PasswordKey} fails the Identity password rules: {string.Join(", ", failedRules)}.");
        }

        var name = configuration[NameKey];
        if (string.IsNullOrWhiteSpace(name))
        {
            name = DefaultName;
        }

        await DevelopmentIdentitySeeder.EnsurePersonAsync(
            db,
            userManager,
            Role,
            name,
            email!,
            password!,
            tenantId: null,
            cancellationToken);

        logger.LogInformation("Bootstrap {Role} {Email} created.", Label, email);
    }

    private static async Task<List<string>> FailedPasswordRulesAsync(
        UserManager<ApplicationUser> userManager,
        string email,
        string password)
    {
        var candidate = new ApplicationUser { UserName = email, Email = email };
        var failed = new List<string>();
        foreach (var validator in userManager.PasswordValidators)
        {
            var result = await validator.ValidateAsync(userManager, candidate, password);
            failed.AddRange(result.Errors.Select(error => error.Code));
        }

        return failed;
    }
}
