extern alias BackOfficeHost;

using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MyWealthV2.Application.FunctionalTests.Hosting;
using MyWealthV2.Application.FunctionalTests.Bff;
using BackOfficeHost::MyWealthV2.BackOffice;

namespace MyWealthV2.Application.FunctionalTests.BackOffice;

public sealed class BackOfficeFactory : WebApplicationFactory<BackOfficeHost::Program>
{
    private readonly string _authority;
    private readonly HttpMessageHandler _identityHandler;
    private readonly string _environment;
    private readonly string? _publicOrigin;
    private readonly IPlatformApi? _api;

    public BackOfficeFactory(
        string authority,
        HttpMessageHandler identityHandler,
        string environment = "Development",
        string? publicOrigin = null,
        IPlatformApi? api = null)
    {
        _authority = authority.TrimEnd('/');
        _identityHandler = identityHandler;
        _environment = environment;
        Settings = string.Equals(environment, "Development", StringComparison.Ordinal)
            ? new Dictionary<string, string>()
            : ProductionSettings.PublicHost();
        _publicOrigin = publicOrigin;
        _api = api;
    }

    // Extra configuration applied when the host starts. Outside Development it starts with the
    // production-hosting keys; tests may change or remove entries before the first request.
    public Dictionary<string, string> Settings { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        foreach (var (key, value) in Settings)
        {
            builder.UseSetting(key, value);
        }
        builder.UseSetting("Authentication:Authority", _authority);
        builder.UseSetting("Authentication:ClientSecret", BackOfficeTestSecret.Value);
        if (!string.IsNullOrWhiteSpace(_publicOrigin))
        {
            builder.UseSetting("Authentication:PublicOrigin", _publicOrigin);
        }

        builder.ConfigureTestServices(services =>
        {
            services.AddTransient<IStartupFilter, TestConnection>();
            services.AddSingleton<IPostConfigureOptions<OpenIdConnectOptions>>(
                new OidcBackchannelPostConfigure(_authority, _identityHandler));
            if (_api is not null)
            {
                services.RemoveAll<IPlatformApi>();
                services.AddSingleton(_api);
            }
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _identityHandler.Dispose();
        }

        base.Dispose(disposing);
    }
}
