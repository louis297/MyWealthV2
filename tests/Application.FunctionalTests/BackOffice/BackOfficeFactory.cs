extern alias BackOfficeHost;

using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MyWealthV2.Application.FunctionalTests.Bff;

namespace MyWealthV2.Application.FunctionalTests.BackOffice;

public sealed class BackOfficeFactory : WebApplicationFactory<BackOfficeHost::Program>
{
    private readonly string _authority;
    private readonly HttpMessageHandler _identityHandler;
    private readonly string _environment;
    private readonly string? _publicOrigin;

    public BackOfficeFactory(
        string authority,
        HttpMessageHandler identityHandler,
        string environment = "Development",
        string? publicOrigin = null)
    {
        _authority = authority.TrimEnd('/');
        _identityHandler = identityHandler;
        _environment = environment;
        _publicOrigin = publicOrigin;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        builder.UseSetting("Authentication:Authority", _authority);
        builder.UseSetting("Authentication:ClientSecret", BackOfficeTestSecret.Value);
        if (!string.IsNullOrWhiteSpace(_publicOrigin))
        {
            builder.UseSetting("Authentication:PublicOrigin", _publicOrigin);
        }

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IPostConfigureOptions<OpenIdConnectOptions>>(
                new OidcBackchannelPostConfigure(_authority, _identityHandler));
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
