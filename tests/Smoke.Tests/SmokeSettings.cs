using Microsoft.Extensions.Configuration;

namespace MyWealthV2.Smoke.Tests;

/// <summary>
/// The <c>Smoke</c> configuration section (docs/smoke-test.md §5).
/// Environment variables (<c>Smoke__AdviserPassword</c>) override user secrets.
/// </summary>
internal sealed class SmokeSettings
{
    public const string TenantCode = "smoke";
    public const string AdviserEmail = "smoke-adviser@localhost";
    public const string CustomerEmail = "smoke-customer@localhost";
    public const string AccountName = "Smoke Cash";

    private const string DefaultAdviserPortalUrl = "https://localhost:7190";
    private const string DefaultCustomerPortalUrl = "https://localhost:7200";

    private static readonly Lazy<IConfiguration> Configuration = new(() => new ConfigurationBuilder()
        .AddUserSecrets<SmokeSettings>(optional: true)
        .AddEnvironmentVariables()
        .Build());

    public static Uri AdviserPortalUrl => Url("Smoke:AdviserPortalUrl", DefaultAdviserPortalUrl);

    public static Uri CustomerPortalUrl => Url("Smoke:CustomerPortalUrl", DefaultCustomerPortalUrl);

    public static string AdviserPassword => Required("Smoke:AdviserPassword");

    public static string CustomerPassword => Required("Smoke:CustomerPassword");

    public static bool TargetsLocalhost =>
        AdviserPortalUrl.IsLoopback || CustomerPortalUrl.IsLoopback;

    private static Uri Url(string key, string fallback)
    {
        var value = Configuration.Value[key];
        return new Uri(string.IsNullOrWhiteSpace(value) ? fallback : value);
    }

    private static string Required(string key)
    {
        var value = Configuration.Value[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            Assert.Fail(
                $"Configuration key '{key}' is not set. Set it with dotnet user-secrets on src/AppHost, " +
                $"or as environment variable '{key.Replace(":", "__")}'.");
        }

        return value!;
    }
}
