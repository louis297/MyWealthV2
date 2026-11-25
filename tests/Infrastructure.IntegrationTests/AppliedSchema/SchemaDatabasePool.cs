using System.Collections.Concurrent;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Schema;
using MyWealthV2.Tests.Common;
using NUnit.Framework.Internal;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

/// <summary>
/// Applied schema databases for the fixtures in this namespace. A fixture leases one the first time it
/// reads <see cref="SchemaDatabaseSetup.ConnectionString"/> and gives it back when the fixture ends, so
/// fixtures that run at the same time never share a database. A database is reused by later fixtures
/// without a reset, as the single shared database was before: tests insert their own rows and tolerate
/// rows from earlier fixtures. The first database is the default one; the others are named after it
/// with a "-pN" suffix and are created the first time no database is free.
/// </summary>
internal sealed class SchemaDatabasePool(string defaultConnectionString)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Stack<AppliedSchemaDatabase> _free = new();
    private readonly ConcurrentDictionary<Test, Task<AppliedSchemaDatabase>> _leases =
        new(ReferenceEqualityComparer.Instance);
    private int _created;

    /// <summary>The database leased to the fixture that is running the current code.</summary>
    public AppliedSchemaDatabase Current
    {
        get
        {
            var fixture = CurrentFixture()
                ?? throw new InvalidOperationException(
                    "The applied schema database is read outside a test fixture.");
            return _leases.GetOrAdd(fixture, _ => LeaseAsync()).GetAwaiter().GetResult();
        }
    }

    /// <summary>Creates and applies the default database, the first one handed out.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        _free.Push(await AppliedSchemaDatabase.CreateAsync(defaultConnectionString, cancellationToken));
        _created = 1;
    }

    /// <summary>Gives the fixture's database back to the pool, if it leased one.</summary>
    public void Return(Test fixture)
    {
        if (!_leases.TryRemove(fixture, out var lease) || !lease.IsCompletedSuccessfully)
        {
            return;
        }

        _gate.Wait();
        try
        {
            _free.Push(lease.Result);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<AppliedSchemaDatabase> LeaseAsync()
    {
        int number;
        await _gate.WaitAsync();
        try
        {
            if (_free.TryPop(out var free))
            {
                return free;
            }

            number = _created++;
        }
        finally
        {
            _gate.Release();
        }

        var builder = new SqlConnectionStringBuilder(defaultConnectionString);
        builder.InitialCatalog += "-p" + number;
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        return await AppliedSchemaDatabase.CreateAsync(builder.ConnectionString, cts.Token);
    }

    private static Test? CurrentFixture()
    {
        for (var test = TestExecutionContext.CurrentContext.CurrentTest; test is not null; test = test.Parent as Test)
        {
            if (test is TestFixture)
            {
                return test;
            }
        }

        return null;
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
