using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MyWealthV2.Tests.Common;
using OpenIddict.Server;

namespace MyWealthV2.Application.FunctionalTests.Infrastructure;

/// <summary>
/// One isolated copy of what the functional tests run against: a database with the applied
/// schema, the WebApi and IdentityHost test servers bound to it, a Respawn resetter and the
/// OIDC test clients. Fixtures that share an environment must not run at the same time.
/// </summary>
internal sealed class TestEnvironment : IAsyncDisposable
{
    private TestEnvironment(
        string connectionString,
        WebApiFactory webApi,
        IdentityFactory identity,
        DatabaseResetter resetter)
    {
        ConnectionString = connectionString;
        WebApi = webApi;
        Identity = identity;
        DbResetter = resetter;
        ScopeFactory = webApi.Services.GetRequiredService<IServiceScopeFactory>();
        WebApiHandler = webApi.Server.CreateHandler();
        WebClient = webApi.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        Oidc = new OpenIdConnectTestClient(() => identity.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        }));

        BackOfficeOidc = new OpenIdConnectTestClient(
            () => identity.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = true
            }),
            clientId: "back-office",
            clientSecret: BackOfficeTestSecret.Value,
            redirectUri: "https://localhost/back-office/signin-oidc");
    }

    public string ConnectionString { get; }
    public WebApiFactory WebApi { get; }
    public IdentityFactory Identity { get; }
    public DatabaseResetter DbResetter { get; }
    public IServiceScopeFactory ScopeFactory { get; }
    public HttpMessageHandler WebApiHandler { get; }
    public HttpClient WebClient { get; }
    public OpenIdConnectTestClient Oidc { get; }
    public OpenIdConnectTestClient BackOfficeOidc { get; }

    /// <summary>
    /// Drops and recreates the database in <paramref name="connectionString"/>, applies the
    /// schema through the WebApi host, then starts the hosts the tests call.
    /// </summary>
    public static async Task<TestEnvironment> CreateAsync(string connectionString, CancellationToken cancellationToken)
    {
        await TestDatabaseRecreator.RecreateAsync(connectionString, cancellationToken);

        using (var schemaHost = new WebApiFactory(connectionString))
        {
            _ = schemaHost.Services;
        }

        var identity = new IdentityFactory(connectionString);
        var identityHandler = identity.Server.CreateHandler();
        var identityAuthority = identity.ClientOptions.BaseAddress!.ToString().TrimEnd('/');

        // In Development, IdentityHost takes its signing and encryption certificates from the user
        // certificate store and creates them the first time its server options are read. On a machine
        // with an empty store (CI), hosts that start in parallel would each create a certificate, and a
        // host could then sign with one certificate and publish another. Reading the options here makes
        // the default environment, which starts alone, create them before any parallel host starts.
        _ = identity.Services.GetRequiredService<IOptionsMonitor<OpenIddictServerOptions>>().CurrentValue;

        var webApi = new WebApiFactory(connectionString, identityAuthority, identityHandler);
        _ = webApi.Services;
        var resetter = await DatabaseResetter.CreateAsync(connectionString);

        return new TestEnvironment(connectionString, webApi, identity, resetter);
    }

    public async ValueTask DisposeAsync()
    {
        WebClient.Dispose();
        WebApiHandler.Dispose();
        await DbResetter.DisposeAsync();
        await WebApi.DisposeAsync();
        await Identity.DisposeAsync();
    }
}
