using Microsoft.Data.SqlClient;
using MyWealthV2.Shared;
using MyWealthV2.Tests.Common;

namespace MyWealthV2.Simulator.Tests;

/// <summary>
/// Starts the TestAppHost SQL Server once and recreates <see cref="Services.TestSimulatorDatabase"/>, so every run
/// starts from an empty simulator database. The simulator process itself is never started by Aspire here:
/// each fixture hosts it in memory with <see cref="SimulatorFactory"/> (simulator spec §9.2).
/// </summary>
[SetUpFixture]
public class SimulatorTestSetup
{
    private static DistributedApplication? _app;

    internal static string ConnectionString { get; private set; } = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var cancellationToken = cts.Token;

        var builder = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.TestAppHost>(
                args: [],
                configureBuilder: (options, _) => { options.DisableDashboard = true; });
        builder.Configuration["ASPIRE_ALLOW_UNSECURED_TRANSPORT"] = "true";

        _app = await builder.BuildAsync(cancellationToken).WaitAsync(cancellationToken);
        await _app.StartAsync(cancellationToken).WaitAsync(cancellationToken);
        await _app.ResourceNotifications.WaitForResourceHealthyAsync(Services.TestSimulatorDatabase, cancellationToken);

        ConnectionString = (await _app.GetConnectionStringAsync(Services.TestSimulatorDatabase, cancellationToken))!;
        await TestDatabaseRecreator.RecreateAsync(ConnectionString, cancellationToken);
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        SqlConnection.ClearAllPools();
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    /// <summary>A connection string for a separate, empty database next to the shared one.</summary>
    internal static async Task<string> CreateScratchDatabaseAsync(string suffix)
    {
        var scratch = new SqlConnectionStringBuilder(ConnectionString)
        {
            InitialCatalog = new SqlConnectionStringBuilder(ConnectionString).InitialCatalog + "-" + suffix
        }.ConnectionString;
        await TestDatabaseRecreator.RecreateAsync(scratch, CancellationToken.None);
        return scratch;
    }
}
