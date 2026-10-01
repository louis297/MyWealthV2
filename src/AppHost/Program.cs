using MyWealthV2.Shared;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddAzureContainerAppEnvironment("aca-env");

var databaseServer = builder
    .AddAzureSqlServer(Services.DatabaseServer)
    .RunAsContainer(container => 
        container.WithLifetime(ContainerLifetime.Persistent))
    .AddDatabase(Services.Database);

var web = builder.AddProject<Projects.Web>(Services.WebApi)
    .WithReference(databaseServer)
    .WaitFor(databaseServer)
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health", endpointName: "http")
    .WithAspNetCoreEnvironment()
    .WithUrlForEndpoint("http", url =>
    {
        url.DisplayText = "Scalar API Reference";
        url.Url = "/scalar";
    });

var identity = builder.AddProject<Projects.IdentityHost>(Services.Identity)
    .WithReference(databaseServer)
    .WaitFor(web)
    .WithExternalHttpEndpoints()
    .WithAspNetCoreEnvironment();

web.WithReference(identity)
    .WithEnvironment("Identity__Authority", identity.GetEndpoint("https"));

var adviserPortalClientSecret = builder.AddParameter("adviser-portal-client-secret", secret: true);

identity.WithEnvironment("Identity__AdviserPortalClientSecret", adviserPortalClientSecret);

var bff = builder.AddProject<Projects.BffAdviserPortal>(Services.BffAdviserPortal)
    .WithReference(web)
    .WithReference(identity)
    .WaitFor(web)
    .WaitFor(identity)
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health", endpointName: "http")
    .WithAspNetCoreEnvironment()
    .WithEnvironment("Authentication__Authority", identity.GetEndpoint("https"))
    .WithEnvironment("Authentication__ClientSecret", adviserPortalClientSecret)
    .WithEnvironment("Api__BaseAddress", "https+http://webapi")
    .WithUrlForEndpoint("http", url =>
    {
        url.DisplayText = "Adviser Portal";
        url.Url = "/";
    });

var portal = builder.AddViteApp(Services.AdviserPortal, "../AdviserPortal")
    .WithExternalHttpEndpoints()
    .WithEnvironment("BROWSER", "none")
    .WithEnvironment("VITE_BFF_HTTP", bff.GetEndpoint("http"))
    .WithUrls(context =>
    {
        foreach (var url in context.Urls)
        {
            url.DisplayLocation = UrlDisplayLocation.DetailsOnly;
        }
    });

bff.WithEnvironment("Spa__DevServerUrl", portal.GetEndpoint("http"));

identity.WithPortalOrigins(bff, builder);

builder.Build().Run();
