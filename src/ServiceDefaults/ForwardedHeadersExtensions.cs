using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.Hosting;

// Forwarded headers for the public hosts behind the TLS-terminating reverse proxy (production-hosting R6–R8).
public static class ForwardedHeadersExtensions
{
    public const string KnownNetworksKey = "ForwardedHeaders:KnownNetworks";

    // Outside Development, reads the trusted networks and fails start-up when the list is empty or invalid.
    public static TBuilder AddPublicHostForwardedHeaders<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        if (builder.Environment.IsDevelopment())
        {
            return builder;
        }

        var networks = ReadKnownNetworks(builder.Configuration);
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            // Not X-Forwarded-Host: the proxy forwards the original Host.
            options.ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
            foreach (var network in networks)
            {
                options.KnownIPNetworks.Add(network);
            }
        });

        return builder;
    }

    // Call first after Build(). In Development, a host that sits behind the Aspire dev proxy passes
    // trustAnyProxyInDevelopment so X-Forwarded-Proto is applied from any address, as before.
    public static WebApplication UsePublicHostForwardedHeaders(
        this WebApplication app,
        bool trustAnyProxyInDevelopment)
    {
        if (!app.Environment.IsDevelopment())
        {
            app.UseForwardedHeaders();
            return app;
        }

        if (!trustAnyProxyInDevelopment)
        {
            return app;
        }

        // The Aspire dev proxy is not always a loopback address, so trust X-Forwarded-Proto.
        // Do not apply X-Forwarded-Host: it rewrites the https://localhost:7190 entry to another name.
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedProto
        };
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        app.UseForwardedHeaders(options);
        return app;
    }

    private static List<System.Net.IPNetwork> ReadKnownNetworks(IConfiguration configuration)
    {
        var values = configuration.GetSection(KnownNetworksKey).Get<string[]>() ?? [];
        if (values.Length == 0)
        {
            throw new InvalidOperationException(
                $"Configuration key '{KnownNetworksKey}' must list at least one CIDR range outside Development.");
        }

        var networks = new List<System.Net.IPNetwork>();
        foreach (var value in values)
        {
            if (!System.Net.IPNetwork.TryParse(value, out var network))
            {
                throw new InvalidOperationException(
                    $"Configuration key '{KnownNetworksKey}' has an entry that is not a valid CIDR range: '{value}'.");
            }

            networks.Add(network);
        }

        return networks;
    }
}
