internal static class AspireExtensions
{
    public static IResourceBuilder<T> WithAspNetCoreEnvironment<T>(this IResourceBuilder<T> builder) 
        where T : IResourceWithEnvironment
    {
        builder.WithEnvironment(context =>
        {
            var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
            context.EnvironmentVariables["ASPNETCORE_ENVIRONMENT"] = environment ?? "Development";
        });

        return builder;
    }

    public static IResourceBuilder<T> WithPortalOrigins<T>(
        this IResourceBuilder<T> identity,
        IResourceBuilder<IResourceWithEndpoints> portal,
        IDistributedApplicationBuilder app)
        where T : IResourceWithEnvironment
    {
        var endpoint = portal.GetEndpoint("http");
        identity.WithEnvironment("Identity__PortalOrigins__0", endpoint);

        var dashboardHost = GetDevLocalhostHost(app);
        if (!string.IsNullOrWhiteSpace(dashboardHost))
        {
            identity.WithEnvironment(
                "Identity__PortalOrigins__1",
                ReferenceExpression.Create(
                    $"{endpoint.Property(EndpointProperty.Scheme)}://{portal.Resource.Name}-{dashboardHost}:{endpoint.Property(EndpointProperty.Port)}"));
        }

        return identity;
    }

    private static string? GetDevLocalhostHost(IDistributedApplicationBuilder app)
    {
        foreach (var key in new[] { "ASPNETCORE_URLS", "applicationUrl" })
        {
            var value = app.Configuration[key];
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            foreach (var part in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (Uri.TryCreate(part, UriKind.Absolute, out var uri) &&
                    uri.Host.EndsWith(".dev.localhost", StringComparison.OrdinalIgnoreCase))
                {
                    return uri.Host;
                }
            }
        }

        return "mywealthv2.dev.localhost";
    }
}