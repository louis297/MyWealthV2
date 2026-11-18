using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Identity;

namespace MyWealthV2.Application.FunctionalTests;

[SetUpFixture]
public class FunctionalTestSetup
{
    internal static IServiceScopeFactory ScopeFactory => Current.ScopeFactory;
    internal static IServiceProvider WebApiServices => Current.WebApi.Services;
    internal static HttpClient WebClient => Current.WebClient;
    internal static HttpMessageHandler WebApiHandler => Current.WebApiHandler;
    internal static OpenIdConnectTestClient Oidc => Current.Oidc;
    internal static OpenIdConnectTestClient BackOfficeOidc => Current.BackOfficeOidc;
    internal static IdentityFactory Identity => Current.Identity;
    internal static DatabaseResetter? DbResetter => _default is null ? null : Current.DbResetter;
    internal static string ConnectionString => Current.ConnectionString;

    private static TestEnvironment? _default;
    private static DistributedApplication? _app;

    // The environment the calling test runs against.
    private static TestEnvironment Current =>
        _default ?? throw new InvalidOperationException("The functional test environment has not started.");

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var cancellationToken = cts.Token;

        var builder = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.TestAppHost>(
                args: [],
                configureBuilder: (options, _) =>
                {
                    options.DisableDashboard = true;
                });

        builder.Configuration["ASPIRE_ALLOW_UNSECURED_TRANSPORT"] = "true";

        _app = await builder
            .BuildAsync(cancellationToken)
            .WaitAsync(cancellationToken);

        await _app
            .StartAsync(cancellationToken)
            .WaitAsync(cancellationToken);

        await _app.ResourceNotifications.WaitForResourceHealthyAsync(
            Services.TestDatabase, cancellationToken);

        var connectionString = (await _app.GetConnectionStringAsync(Services.TestDatabase))!;
        _default = await TestEnvironment.CreateAsync(connectionString, cancellationToken);
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        if (_default is not null) await _default.DisposeAsync();
        if (_app is not null) await _app.DisposeAsync();
    }

    internal static async Task ReseedAsync()
    {
        using var scope = ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO [Currencies] ([Code], [Name], [DecimalPlaces], [IsActive]) VALUES
                ('AUD', N'Australian Dollar', 2, 1),
                ('EUR', N'Euro', 2, 1),
                ('GBP', N'Pound Sterling', 2, 1),
                ('JPY', N'Japanese Yen', 0, 1),
                ('NZD', N'New Zealand Dollar', 2, 1),
                ('USD', N'United States Dollar', 2, 1)
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO [Reports] ([PublicId], [Code], [Name], [Function], [Scope], [AllowedTriggers], [IsActive]) VALUES
                ('5a9d3c70-2e84-4b1f-8c36-7f1e0a9d5b22', N'adviser-balances', N'Adviser Balances Report', N'Point-in-time cash and holding lines for an adviser''s customers.', N'Book', N'["manual","scheduled"]', 1),
                ('8c4e1a20-7b3d-4f6a-9e15-2d0c6b8a4f11', N'customer-transactions', N'Customer Transactions Report', N'Transactions in a date range for one customer.', N'Customer', N'["manual","scheduled"]', 1)
            """);
        await scope.ServiceProvider.GetRequiredService<ICurrencyCatalog>().ReloadAsync();
        await DevelopmentIdentitySeeder.SeedAsync(scope.ServiceProvider, CancellationToken.None);
    }
}
