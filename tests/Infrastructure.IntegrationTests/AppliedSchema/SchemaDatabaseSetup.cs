using Microsoft.EntityFrameworkCore;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Data.Schema;
using MyWealthV2.Shared;
using NUnit.Framework;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

[SetUpFixture]
public class SchemaDatabaseSetup
{
    internal static string ConnectionString { get; private set; } = null!;

    private static DistributedApplication? _app;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
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
            Services.TestDatabase, cancellationToken);

        ConnectionString = (await _app.GetConnectionStringAsync(Services.TestDatabase))!;

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        await using var db = new ApplicationDbContext(options);
        var schemaDirectory = Path.Combine(AppContext.BaseDirectory, "schema");
        var applicator = new SchemaApplicator(
            new FileSchemaScriptSource(schemaDirectory),
            new EfSchemaVersionStore(db),
            new EfSqlBatchExecutor(db));

        await applicator.ApplyAsync(cancellationToken);
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }
}
