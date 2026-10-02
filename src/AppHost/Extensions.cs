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
        IResourceBuilder<IResourceWithEndpoints> portal)
        where T : IResourceWithEnvironment
    {
        // The browser entry is the https endpoint (https://localhost:7190). Do not invent a
        // *.dev.localhost alias, and do not register the http endpoint.
        identity.WithEnvironment("Identity__PortalOrigins__0", portal.GetEndpoint("https"));
        return identity;
    }
}