extern alias BffCustomer;

using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MyWealthV2.Application.FunctionalTests.Hosting;
using BffCustomer::MyWealthV2.BffCustomerPortal;

namespace MyWealthV2.Application.FunctionalTests.BffCustomer;

public sealed class CustomerBffFactory : WebApplicationFactory<BffCustomer::Program>
{
    private readonly string _authority;
    private readonly HttpMessageHandler _identityHandler;
    private readonly string _environment;
    private readonly IWebApiTransport? _api;
    private readonly string? _apiBaseAddress;
    private readonly string? _publicOrigin;
    private readonly string? _spaDevServerUrl;
    private readonly string? _webRoot;
    private readonly TimeProvider? _time;

    public CustomerBffFactory(
        string authority,
        HttpMessageHandler identityHandler,
        string environment = "Development",
        IWebApiTransport? api = null,
        string? apiBaseAddress = null,
        string? publicOrigin = null,
        string? spaDevServerUrl = null,
        string? webRoot = null,
        TimeProvider? time = null)
    {
        _authority = authority.TrimEnd('/');
        _identityHandler = identityHandler;
        _environment = environment;
        Settings = string.Equals(environment, "Development", StringComparison.Ordinal)
            ? new Dictionary<string, string>()
            : ProductionSettings.PublicHost();
        _api = api;
        _apiBaseAddress = apiBaseAddress;
        _publicOrigin = publicOrigin;
        _spaDevServerUrl = spaDevServerUrl;
        _webRoot = webRoot;
        _time = time;
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
        builder.UseSetting("Authentication:ClientSecret", CustomerPortalTestSecret.Value);
        if (!string.IsNullOrWhiteSpace(_apiBaseAddress))
        {
            builder.UseSetting("Api:BaseAddress", _apiBaseAddress);
        }

        if (!string.IsNullOrWhiteSpace(_spaDevServerUrl))
        {
            builder.UseSetting("Spa:DevServerUrl", _spaDevServerUrl);
        }

        if (!string.IsNullOrWhiteSpace(_webRoot))
        {
            builder.UseSetting(WebHostDefaults.WebRootKey, _webRoot);
        }

        if (!string.IsNullOrWhiteSpace(_publicOrigin))
        {
            builder.UseSetting("Authentication:PublicOrigin", _publicOrigin);
        }

        builder.ConfigureTestServices(services =>
        {
            services.AddTransient<IStartupFilter, TestConnection>();
            services.AddSingleton<IPostConfigureOptions<OpenIdConnectOptions>>(
                new MyWealthV2.Application.FunctionalTests.Bff.OidcBackchannelPostConfigure(_authority, _identityHandler));
            if (_api is not null)
            {
                services.AddSingleton(_api);
            }

            if (_time is not null)
            {
                services.AddSingleton(_time);
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
