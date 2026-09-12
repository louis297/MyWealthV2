using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Infrastructure.Identity;

namespace MyWealthV2.Application.FunctionalTests;

[SetUpFixture]
public class FunctionalTestSetup
{
    internal static IServiceScopeFactory ScopeFactory { get; private set; } = null!;
    internal static HttpClient WebClient { get; private set; } = null!;
    internal static OpenIdConnectTestClient Oidc { get; private set; } = null!;
    internal static DatabaseResetter? DbResetter { get; private set; }
    internal static string ConnectionString { get; private set; } = null!;

    private static WebApiFactory? _factory;
    private static IdentityFactory? _identityFactory;
    private static DistributedApplication? _app;

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
            Services.Database, cancellationToken);

        var connectionString = (await _app.GetConnectionStringAsync(Services.Database))!;
        ConnectionString = connectionString;

        using (var schemaHost = new WebApiFactory(connectionString))
        {
            _ = schemaHost.Services;
        }

        var identityFactory = new IdentityFactory(connectionString);
        _identityFactory = identityFactory;
        var identityHandler = identityFactory.Server.CreateHandler();
        var identityAuthority = identityFactory.ClientOptions.BaseAddress!.ToString().TrimEnd('/');

        _factory = new WebApiFactory(connectionString, identityAuthority, identityHandler);
        ScopeFactory = _factory.Services.GetRequiredService<IServiceScopeFactory>();
        DbResetter = await DatabaseResetter.CreateAsync(connectionString);

        WebClient = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        Oidc = new OpenIdConnectTestClient(() => identityFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        }));
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        WebClient?.Dispose();
        if (DbResetter is not null) await DbResetter.DisposeAsync();
        if (_factory is not null) await _factory.DisposeAsync();
        if (_identityFactory is not null) await _identityFactory.DisposeAsync();
        if (_app is not null) await _app.DisposeAsync();
    }

    internal static async Task ReseedAsync()
    {
        using var scope = ScopeFactory.CreateScope();
        await DevelopmentIdentitySeeder.SeedAsync(scope.ServiceProvider, CancellationToken.None);
    }
}
