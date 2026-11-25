using System.Collections.Concurrent;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Schema;
using MyWealthV2.Tests.Common;
using NUnit.Framework.Internal;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

/// <summary>
/// Applied schema databases for the fixtures in this namespace, one per NUnit test worker. A worker runs one
/// fixture at a time, and the test cases of a fixture run on the worker that runs the fixture, so fixtures that
/// run at the same time never share a database. A database is reused by the later fixtures of its worker
/// without a reset, as the single shared database was before: tests insert their own rows and tolerate rows
/// from earlier fixtures. The first database is the default one; the others are named after it with a "-pN"
/// suffix and are created the first time a worker needs one.
/// </summary>
internal sealed class SchemaDatabasePool(string defaultConnectionString)
{
    /// <summary>Key for code that runs outside a test worker, as when NUnit runs without parallel workers.</summary>
    private static readonly object NoWorker = new();

    private readonly ConcurrentDictionary<object, Lazy<Task<AppliedSchemaDatabase>>> _databases = new();
    private int _created;

    /// <summary>The database of the test worker that is running the current code.</summary>
    public AppliedSchemaDatabase Current
    {
        get
        {
            var worker = (object?)TestExecutionContext.CurrentContext.TestWorker ?? NoWorker;
            return _databases.GetOrAdd(worker, _ => new Lazy<Task<AppliedSchemaDatabase>>(CreateAsync))
                .Value.GetAwaiter().GetResult();
        }
    }

    /// <summary>Drops the "-pN" databases that earlier runs left on the persistent test server.</summary>
    public async Task DropLeftoverDatabasesAsync(CancellationToken cancellationToken)
    {
        var builder = new SqlConnectionStringBuilder(defaultConnectionString);
        var pattern = builder.InitialCatalog.Replace("[", "[[]").Replace("_", "[_]").Replace("%", "[%]") + "-p%";
        builder.InitialCatalog = "master";

        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var names = new List<string>();
        await using (var list = connection.CreateCommand())
        {
            list.CommandText = "SELECT [name] FROM sys.databases WHERE [name] LIKE @pattern";
            list.Parameters.AddWithValue("@pattern", pattern);
            await using var reader = await list.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                names.Add(reader.GetString(0));
            }
        }

        foreach (var name in names)
        {
            var quoted = "[" + name.Replace("]", "]]") + "]";
            await using var drop = connection.CreateCommand();
            drop.CommandText = $"ALTER DATABASE {quoted} SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE {quoted};";
            await drop.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private async Task<AppliedSchemaDatabase> CreateAsync()
    {
        var number = Interlocked.Increment(ref _created) - 1;
        var builder = new SqlConnectionStringBuilder(defaultConnectionString);
        if (number > 0)
        {
            builder.InitialCatalog += "-p" + number;
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        return await AppliedSchemaDatabase.CreateAsync(builder.ConnectionString, cts.Token);
    }
}

/// <summary>A database with every schema script applied, and its row counts straight after the apply.</summary>
internal sealed record AppliedSchemaDatabase(
    string ConnectionString,
    int TransactionsAfterApply,
    int TransactionCashLegsAfterApply,
    int IdempotencyRecordsAfterApply,
    int TenantReportsAfterApply)
{
    public static async Task<AppliedSchemaDatabase> CreateAsync(string connectionString, CancellationToken cancellationToken)
    {
        await TestDatabaseRecreator.RecreateAsync(connectionString, cancellationToken);

        var schemaDirectory = Path.Combine(AppContext.BaseDirectory, "schema");
        await SchemaApplicator.ForDatabase(connectionString, schemaDirectory).ApplyAsync(cancellationToken);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        await using var db = new ApplicationDbContext(options);

        // Fixtures insert rows into this database. Seed checks use the counts from straight after the apply.
        return new AppliedSchemaDatabase(
            connectionString,
            await db.Transactions.CountAsync(cancellationToken),
            await db.TransactionCashLegs.CountAsync(cancellationToken),
            await db.IdempotencyRecords.CountAsync(cancellationToken),
            await db.TenantReports.CountAsync(cancellationToken));
    }
}
