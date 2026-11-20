using MyWealthV2.Shared;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddAzureContainerAppEnvironment("aca-env");

var sqlServer = builder
    .AddAzureSqlServer(Services.DatabaseServer)
    .RunAsContainer(container => 
        container.WithLifetime(ContainerLifetime.Persistent));

var databaseServer = sqlServer.AddDatabase(Services.Database);

// Simulator spec R14: the simulator's own database on the same server. No platform host references it.
var simulatorDatabase = sqlServer.AddDatabase(Services.SimulatorDatabase);

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

// Smoke tenant passwords (docs/smoke-test.md §5). Set in user secrets as Smoke:AdviserPassword and
// Smoke:CustomerPassword; tests/Smoke.Tests reads the same store. Missing → webapi skips the smoke seed.
if (!string.IsNullOrWhiteSpace(builder.Configuration["Smoke:AdviserPassword"])
    && !string.IsNullOrWhiteSpace(builder.Configuration["Smoke:CustomerPassword"]))
{
    var smokeAdviserPassword = builder.AddParameterFromConfiguration(
        "smoke-adviser-password", "Smoke:AdviserPassword", secret: true);
    var smokeCustomerPassword = builder.AddParameterFromConfiguration(
        "smoke-customer-password", "Smoke:CustomerPassword", secret: true);
    web.WithEnvironment("Smoke__AdviserPassword", smokeAdviserPassword)
        .WithEnvironment("Smoke__CustomerPassword", smokeCustomerPassword);
}

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

var customerPortalClientSecret = builder.AddParameter("customer-portal-client-secret", secret: true);

identity.WithEnvironment("Identity__CustomerPortalClientSecret", customerPortalClientSecret);

var customerBff = builder.AddProject<Projects.BffCustomerPortal>(Services.BffCustomerPortal)
    .WithReference(web)
    .WithReference(identity)
    .WaitFor(web)
    .WaitFor(identity)
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health", endpointName: "http")
    .WithAspNetCoreEnvironment()
    .WithEnvironment("Authentication__Authority", identity.GetEndpoint("https"))
    .WithEnvironment("Authentication__ClientSecret", customerPortalClientSecret)
    .WithEnvironment("Api__BaseAddress", "https+http://webapi")
    .WithUrlForEndpoint("https", url =>
    {
        url.DisplayText = "Customer Portal";
        url.Url = "/";
    })
    .WithUrlForEndpoint("http", url =>
    {
        url.DisplayLocation = UrlDisplayLocation.DetailsOnly;
    });

customerBff.WithEnvironment("Authentication__PublicOrigin", customerBff.GetEndpoint("https"));
identity.WithEnvironment("Identity__CustomerPortalOrigins__0", customerBff.GetEndpoint("https"));

var customerPortal = builder.AddViteApp(Services.CustomerPortal, "../CustomerPortal")
    .WithExternalHttpEndpoints()
    .WithEnvironment("BROWSER", "none")
    .WithEnvironment("VITE_BFF_HTTP", customerBff.GetEndpoint("https"))
    .WithUrls(context =>
    {
        foreach (var url in context.Urls)
        {
            url.DisplayLocation = UrlDisplayLocation.DetailsOnly;
        }
    });

customerBff.WithEnvironment("Spa__DevServerUrl", customerPortal.GetEndpoint("http"));

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

// Simulator spec R6: identity seeds the fourth client from this secret. The simulator resource uses it too.
var simulatorClientSecret = builder.AddParameter("simulator-client-secret", secret: true);

identity.WithEnvironment("Identity__SimulatorClientSecret", simulatorClientSecret);

// Simulator spec §9.2. The party endpoint is for the platform (roadmap B3) and is not external. Only worker
// receives the party key: it reads the simulator bank through bank connection "simulator" (deposit-matching §9.1).
var simulatorPartyApiKey = builder.AddParameter("simulator-party-api-key", secret: true);

var simulator = builder.AddProject<Projects.Simulator>(Services.Simulator)
    .WithReference(simulatorDatabase)
    .WithReference(identity)
    .WaitFor(simulatorDatabase)
    .WaitFor(identity)
    .WithHttpEndpoint(name: "party")
    .WithExternalHttpEndpoints()
    .WithEndpoint("party", endpoint => endpoint.IsExternal = false)
    .WithHttpHealthCheck("/health", endpointName: "http")
    .WithAspNetCoreEnvironment()
    .WithEnvironment("Authentication__Authority", identity.GetEndpoint("https"))
    .WithEnvironment("Authentication__ClientSecret", simulatorClientSecret)
    .WithEnvironment("Simulator__PartyApiKey", simulatorPartyApiKey)
    .WithUrlForEndpoint("https", url =>
    {
        url.DisplayText = "Simulator";
        url.Url = "/scalar";
    })
    .WithUrlForEndpoint("party", url =>
    {
        url.DisplayLocation = UrlDisplayLocation.DetailsOnly;
    });

// Deposit matching R39, §9.1: the simulator bank opens the trust accounts TestSeed gives its client money accounts.
string[][] standingAccounts =
[
    ["SIM-DEMO-USD", "USD", "Demo Advisory Client Funds Trust Account"],
    ["SIM-DEMO-NZD", "NZD", "Demo Advisory Client Funds Trust Account"],
    ["SIM-DEMO2-USD", "USD", "Demo Advisory 2 Client Funds Trust Account"],
    ["SIM-DEMO2-NZD", "NZD", "Demo Advisory 2 Client Funds Trust Account"]
];
for (var i = 0; i < standingAccounts.Length; i++)
{
    simulator.WithEnvironment($"Simulator__Bank__StandingAccounts__{i}__AccountNumber", standingAccounts[i][0])
        .WithEnvironment($"Simulator__Bank__StandingAccounts__{i}__Currency", standingAccounts[i][1])
        .WithEnvironment($"Simulator__Bank__StandingAccounts__{i}__Name", standingAccounts[i][2]);
}

// Simulator spec R12: the host tells the party port from the control ports by the port Kestrel listens on.
simulator.WithEnvironment("Simulator__PartyPort", simulator.GetEndpoint("party").Property(EndpointProperty.TargetPort));
simulator.WithEnvironment("Authentication__PublicOrigin", simulator.GetEndpoint("https"));
identity.WithEnvironment("Identity__SimulatorOrigin", simulator.GetEndpoint("https"));

var eodhdApiKey = builder.AddParameter("eodhd-api-key", secret: true);

// Deposit matching R1, §9.1: webapi knows only the connection name and adapter; worker also gets the URL and the
// key. worker does not wait for the simulator: a missing party is a normal state (ADR 0021 rule 15).
web.WithEnvironment("BankConnections__simulator__Adapter", "Simulator");

builder.AddProject<Projects.Worker>(Services.Worker)
    .WithReference(databaseServer)
    .WaitFor(databaseServer)
    .WithEnvironment("BankConnections__simulator__Adapter", "Simulator")
    .WithEnvironment("BankConnections__simulator__BaseUrl", simulator.GetEndpoint("party"))
    .WithEnvironment("BankConnections__simulator__ApiKey", simulatorPartyApiKey)
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health", endpointName: "http")
    .WithAspNetCoreEnvironment()
    .WithEnvironment("Reports__StorageRoot", reportsPath)
    .WithEnvironment("Eodhd__ApiKey", eodhdApiKey)
    //.WithVolume("reports", "/data/reports", env: "Reports__StorageRoot")
    .WithUrlForEndpoint("http", url =>
    {
        url.DisplayText = "Hangfire";
        url.Url = "/hangfire";
    });

builder.Build().Run();
