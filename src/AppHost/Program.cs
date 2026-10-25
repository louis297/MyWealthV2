using MyWealthV2.Shared;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddAzureContainerAppEnvironment("aca-env");

var databaseServer = builder
    .AddAzureSqlServer(Services.DatabaseServer)
    .RunAsContainer(container => 
        container.WithLifetime(ContainerLifetime.Persistent))
    .AddDatabase(Services.Database);

var solutionRoot = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", ".."));
var reportsPath = Path.Combine(solutionRoot, "artifacts", "reports");
Directory.CreateDirectory(reportsPath);

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
    })
    //.WithBindMount(reportsPath, "/data/reports")
    .WithEnvironment("Reports__StorageRoot", reportsPath);
    //.WithVolume("reports", "/data/reports", env: "Reports__StorageRoot");

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
    .WithUrlForEndpoint("https", url =>
    {
        url.DisplayText = "Adviser Portal";
        url.Url = "/";
    })
    .WithUrlForEndpoint("http", url =>
    {
        url.DisplayLocation = UrlDisplayLocation.DetailsOnly;
    });

var portal = builder.AddViteApp(Services.AdviserPortal, "../AdviserPortal")
    .WithExternalHttpEndpoints()
    .WithEnvironment("BROWSER", "none")
    .WithEnvironment("VITE_BFF_HTTP", bff.GetEndpoint("https"))
    .WithUrls(context =>
    {
        foreach (var url in context.Urls)
        {
            url.DisplayLocation = UrlDisplayLocation.DetailsOnly;
        }
    });

bff.WithEnvironment("Spa__DevServerUrl", portal.GetEndpoint("http"));
bff.WithEnvironment("Authentication__PublicOrigin", bff.GetEndpoint("https"));

identity.WithPortalOrigins(bff);

var backOfficeClientSecret = builder.AddParameter("back-office-client-secret", secret: true);

identity.WithEnvironment("Identity__BackOfficeClientSecret", backOfficeClientSecret);

var backOffice = builder.AddProject<Projects.BackOffice>(Services.BackOffice)
    .WithReference(web)
    .WithReference(identity)
    .WaitFor(web)
    .WaitFor(identity)
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health", endpointName: "http")
    .WithAspNetCoreEnvironment()
    .WithEnvironment("Authentication__Authority", identity.GetEndpoint("https"))
    .WithEnvironment("Authentication__ClientSecret", backOfficeClientSecret)
    .WithEnvironment("Api__BaseAddress", "https+http://webapi")
    .WithUrlForEndpoint("https", url =>
    {
        url.DisplayText = "Back Office";
        url.Url = "/";
    });

backOffice.WithEnvironment("Authentication__PublicOrigin", backOffice.GetEndpoint("https"));
identity.WithEnvironment("Identity__BackOfficeOrigin", backOffice.GetEndpoint("https"));

builder.AddProject<Projects.Worker>(Services.Worker)
    .WithReference(databaseServer)
    .WaitFor(databaseServer)
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health", endpointName: "http")
    .WithAspNetCoreEnvironment()
    .WithEnvironment("Reports__StorageRoot", reportsPath)
    //.WithVolume("reports", "/data/reports", env: "Reports__StorageRoot")
    .WithUrlForEndpoint("http", url =>
    {
        url.DisplayText = "Hangfire";
        url.Url = "/hangfire";
    });

builder.Build().Run();
