extern alias BffCustomer;

using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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

    public CustomerBffFactory(
        string authority,
        HttpMessageHandler identityHandler,
        string environment = "Development",
        IWebApiTransport? api = null,
        string? apiBaseAddress = null,
        string? publicOrigin = null)
    {
        _authority = authority.TrimEnd('/');
        _identityHandler = identityHandler;
        _environment = environment;
        _api = api;
        _apiBaseAddress = apiBaseAddress;
        _publicOrigin = publicOrigin;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        builder.UseSetting("Authentication:Authority", _authority);
        builder.UseSetting("Authentication:ClientSecret", CustomerPortalTestSecret.Value);
        if (!string.IsNullOrWhiteSpace(_apiBaseAddress))
        {
            builder.UseSetting("Api:BaseAddress", _apiBaseAddress);
        }

        if (!string.IsNullOrWhiteSpace(_publicOrigin))
        {
            builder.UseSetting("Authentication:PublicOrigin", _publicOrigin);
        }

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IPostConfigureOptions<OpenIdConnectOptions>>(
                new MyWealthV2.Application.FunctionalTests.Bff.OidcBackchannelPostConfigure(_authority, _identityHandler));
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
