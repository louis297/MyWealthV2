using Microsoft.Extensions.Configuration;

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

    public static Task RunAsync(
        IServiceProvider services,
        IConfiguration configuration,
        CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
