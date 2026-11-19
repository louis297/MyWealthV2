using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Simulator.Tests.Hosting;

namespace MyWealthV2.Simulator.Tests;

/// <summary>
/// The simulator host in memory, on the simulator test database. Requests choose the port they arrive on with
/// <see cref="TestConnection.LocalPortHeader"/>; <see cref="ControlClient"/> and <see cref="PartyClient"/> set it.
/// </summary>
public sealed class SimulatorFactory(
    string? connectionString = null,
    string environment = "Development",
    IReadOnlyDictionary<string, string?>? settings = null) : WebApplicationFactory<Program>
{
    public const int ControlPort = 8080;
    public const int HealthPort = 8081;
    public const int PartyPort = 8082;

    /// <summary>The machine credential the test host is configured with (simulator spec R30, R31).</summary>
    public const string PartyApiKey = "test-party-api-key-0123456789abcdef";

    public const string Authority = "https://identity.test";

    public const string PublicOrigin = "https://sim.test";

    public HttpClient ControlClient() => ClientOn(ControlPort);

    public HttpClient PartyClient(string? apiKey = PartyApiKey)
    {
        var client = ClientOn(PartyPort);
        if (apiKey is not null)
        {
            client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        }

        return client;
    }

    public HttpClient ClientOn(int port)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(TestConnection.LocalPortHeader, port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.UseSetting($"ConnectionStrings:{MyWealthV2.Shared.Services.SimulatorDatabase}", connectionString ?? SimulatorTestSetup.ConnectionString);
        builder.UseSetting("Simulator:PartyPort", PartyPort.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.UseSetting("Simulator:PartyApiKey", PartyApiKey);
        builder.UseSetting("Authentication:Authority", Authority);
        builder.UseSetting("Authentication:ClientSecret", "test-simulator-secret");
        builder.UseSetting("Authentication:PublicOrigin", PublicOrigin);
        if (environment != "Development")
        {
            builder.UseSetting("ForwardedHeaders:KnownNetworks:0", "10.0.0.0/8");
            builder.UseSetting("DataProtection:KeysPath", Directory.CreateTempSubdirectory("simulator-keys-").FullName);
        }

        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.AddTransient<IStartupFilter, TestConnection>();
        });
    }
}
