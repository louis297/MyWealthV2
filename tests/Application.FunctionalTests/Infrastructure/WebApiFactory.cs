extern alias WebHost;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MyWealthV2.Application.FunctionalTests.Infrastructure;

public class WebApiFactory(
    string connectionString,
    string? identityAuthority = null,
    HttpMessageHandler? identityBackchannel = null) : WebApplicationFactory<WebHost::Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting($"ConnectionStrings:{MyWealthV2.Shared.Services.Database}", connectionString);
        builder.UseEnvironment("Development");

        if (!string.IsNullOrWhiteSpace(identityAuthority))
        {
            builder.UseSetting("Identity:Authority", identityAuthority);
        }

        builder.ConfigureTestServices(services =>
        {
            if (identityBackchannel is not null && !string.IsNullOrWhiteSpace(identityAuthority))
            {
                var authority = identityAuthority.TrimEnd('/');
                var handler = identityBackchannel;
                services.AddSingleton<IPostConfigureOptions<JwtBearerOptions>>(
                    new JwtBearerBackchannelPostConfigure(authority, handler));
            }
        });
    }
}

internal sealed class JwtBearerBackchannelPostConfigure(string authority, HttpMessageHandler handler)
    : IPostConfigureOptions<JwtBearerOptions>
{
    public void PostConfigure(string? name, JwtBearerOptions options)
    {
        options.Authority = authority;
        options.MetadataAddress = authority + "/.well-known/openid-configuration";
        options.RequireHttpsMetadata = false;
        options.BackchannelHttpHandler = handler;
    }
}
