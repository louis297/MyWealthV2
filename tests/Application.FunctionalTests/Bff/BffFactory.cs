extern alias BffHost;

using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using BffHost::MyWealthV2.BffAdviserPortal;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace MyWealthV2.Application.FunctionalTests.Bff;

public sealed class BffFactory : WebApplicationFactory<BffHost::Program>
{
    private readonly string _authority;
    private readonly HttpMessageHandler _identityHandler;
    private readonly string _environment;
    private readonly IWebApiTransport? _api;
    private readonly string? _apiBaseAddress;
    private readonly string? _spaDevServerUrl;

    public BffFactory(
        string authority,
        HttpMessageHandler identityHandler,
        string environment = "Development",
        IWebApiTransport? api = null,
        string? apiBaseAddress = null,
        string? spaDevServerUrl = null)
    {
        _authority = authority.TrimEnd('/');
        _identityHandler = identityHandler;
        _environment = environment;
        _api = api;
        _apiBaseAddress = apiBaseAddress;
        _spaDevServerUrl = spaDevServerUrl;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        builder.UseSetting("Authentication:Authority", _authority);
        builder.UseSetting("Authentication:ClientSecret", AdviserPortalTestSecret.Value);
        if (!string.IsNullOrWhiteSpace(_apiBaseAddress))
        {
            builder.UseSetting("Api:BaseAddress", _apiBaseAddress);
        }

        if (!string.IsNullOrWhiteSpace(_spaDevServerUrl))
        {
            builder.UseSetting("Spa:DevServerUrl", _spaDevServerUrl);
        }

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IPostConfigureOptions<OpenIdConnectOptions>>(
                new OidcBackchannelPostConfigure(_authority, _identityHandler));
            if (_api is not null)
            {
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

internal sealed class OidcBackchannelPostConfigure(string authority, HttpMessageHandler handler)
    : IPostConfigureOptions<OpenIdConnectOptions>
{
    public void PostConfigure(string? name, OpenIdConnectOptions options)
    {
        if (!string.IsNullOrEmpty(name) &&
            !string.Equals(name, OpenIdConnectDefaults.AuthenticationScheme, StringComparison.Ordinal))
        {
            return;
        }

        options.Authority = authority;
        options.MetadataAddress = authority + "/.well-known/openid-configuration";
        options.RequireHttpsMetadata = false;
        options.BackchannelHttpHandler = handler;
        options.Backchannel = new HttpClient(handler, disposeHandler: false)
        {
            Timeout = options.BackchannelTimeout,
            MaxResponseContentBufferSize = 1024 * 1024 * 10
        };
        options.ConfigurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            options.MetadataAddress,
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever(options.Backchannel) { RequireHttps = false })
        {
            RefreshInterval = options.RefreshInterval,
            AutomaticRefreshInterval = options.AutomaticRefreshInterval
        };
    }
}
