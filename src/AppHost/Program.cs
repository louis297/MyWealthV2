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

var portal = builder.AddViteApp(Services.AdviserPortal, "../AdviserPortal")
    .WithExternalHttpEndpoints()
    .WithEnvironment("VITE_IDENTITY_AUTHORITY", identity.GetEndpoint("https"))
    .WithEnvironment("VITE_WEBAPI_BASE_URL", web.GetEndpoint("https"))
    .WithEnvironment("BROWSER", "none")
    .WaitFor(web)
    .WaitFor(identity);

identity.WithPortalOrigins(portal, builder);

builder.Build().Run();
