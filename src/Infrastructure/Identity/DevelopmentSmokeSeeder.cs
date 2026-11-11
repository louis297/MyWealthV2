namespace MyWealthV2.Infrastructure.Identity;

public static class DevelopmentSmokeSeeder
{
    public const string TenantCode = "smoke";
    public const string TenantName = "Smoke Test";
    public const string AdviserEmail = "smoke-adviser@localhost";
    public const string AdviserName = "Smoke Adviser";
    public const string CustomerEmail = "smoke-customer@localhost";
    public const string CustomerName = "Smoke Customer";
    public const string AccountName = "Smoke Cash";

    public static Task SeedAsync(
        IServiceProvider services,
        string? adviserPassword,
        string? customerPassword,
        CancellationToken cancellationToken) => Task.CompletedTask;
}
