using MyWealthV2.Shared;
using NUnit.Framework;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

[SetUpFixture]
public class SchemaDatabaseSetup
{
    private static DistributedApplication? _app;

    private static SchemaDatabasePool? _pool;

    /// <summary>The applied schema database of the current fixture (one per test worker, see <see cref="SchemaDatabasePool"/>).</summary>
    internal static string ConnectionString => Pool.Current.ConnectionString;

    internal static int TransactionsAfterApply => Pool.Current.TransactionsAfterApply;

    internal static int TransactionCashLegsAfterApply => Pool.Current.TransactionCashLegsAfterApply;

    internal static int IdempotencyRecordsAfterApply => Pool.Current.IdempotencyRecordsAfterApply;

    internal static int TenantReportsAfterApply => Pool.Current.TenantReportsAfterApply;

    /// <summary>The default database's connection string, for statements that only need the server.</summary>
    internal static string ServerConnectionString { get; private set; } = null!;

    private static SchemaDatabasePool Pool =>
        _pool ?? throw new InvalidOperationException("The schema test database is not set up.");

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var cancellationToken = cts.Token;

        var builder = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.TestAppHost>(
                args: [],
                configureBuilder: (options, _) =>
                {
                    options.DisableDashboard = true;
                });

        builder.Configuration["ASPIRE_ALLOW_UNSECURED_TRANSPORT"] = "true";

        _app = await builder
            .BuildAsync(cancellationToken)
            .WaitAsync(cancellationToken);

        await _app
            .StartAsync(cancellationToken)
            .WaitAsync(cancellationToken);

        await _app.ResourceNotifications.WaitForResourceHealthyAsync(
            Services.SchemaTestDatabase, cancellationToken);

        ServerConnectionString = (await _app.GetConnectionStringAsync(Services.SchemaTestDatabase, cancellationToken))!;
        var pool = new SchemaDatabasePool(ServerConnectionString);
        await pool.DropLeftoverDatabasesAsync(cancellationToken);
        _pool = pool;
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        _pool = null;
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }
}
