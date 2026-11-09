using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Data.Schema;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

public class LegAccountsSchemaTests
{
    private const string LegAccountsScript = "0024_leg_accounts.sql";

    [Test]
    public void SchemaDirectory_Includes0024LegAccountsScript()
    {
        new FileSchemaScriptSource(SchemaDirectory()).GetScripts()
            .Select(script => script.Name)
            .ShouldContain(LegAccountsScript);
    }

    [Test]
    public void Model_PutsAccountOnLegsAndNotOnTheHeader()
    {
        using var db = OpenDb(SchemaDatabaseSetup.ConnectionString);

        var header = db.Model.FindEntityType(typeof(Transaction))!;
        header.FindProperty("AccountId").ShouldBeNull();
        header.GetForeignKeys()
            .ShouldNotContain(key => key.PrincipalEntityType.ClrType == typeof(Account));

        foreach (var legType in new[] { typeof(TransactionCashLeg), typeof(TransactionSecurityLeg) })
        {
            var leg = db.Model.FindEntityType(legType)!;
            var accountId = leg.FindProperty("AccountId");
            accountId.ShouldNotBeNull();
            accountId.IsNullable.ShouldBeFalse();
            leg.GetForeignKeys()
                .Single(key => key.PrincipalEntityType.ClrType == typeof(Account))
                .DeleteBehavior.ShouldBe(DeleteBehavior.Restrict);
        }
    }

    [Test]
    public async Task Apply_SecurityLegAccountIndexCoversHoldingProjection()
    {
        await using var db = OpenDb(SchemaDatabaseSetup.ConnectionString);
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT COL_NAME(ic.object_id, ic.column_id)
            FROM sys.indexes i
            JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
            WHERE i.object_id = OBJECT_ID(N'TransactionSecurityLegs')
              AND i.name = N'IX_TransactionSecurityLegs_AccountId_InstrumentId'
              AND ic.is_included_column = 0
            ORDER BY ic.key_ordinal
            """;

        var columns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(0));
        }

        columns.ShouldBe(["AccountId", "InstrumentId"]);
    }

    [Test]
    public async Task Apply0024_BackFillsEveryLegFromItsHeaderAccount()
    {
        new FileSchemaScriptSource(SchemaDirectory()).GetScripts()
            .Select(script => script.Name)
            .ShouldContain(LegAccountsScript);

        var databaseName = $"LegAccountsBackfill_{Guid.NewGuid():N}";
        var connectionString = new SqlConnectionStringBuilder(SchemaDatabaseSetup.ConnectionString)
        {
            InitialCatalog = databaseName
        }.ConnectionString;

        await ExecuteOnMasterAsync($"CREATE DATABASE [{databaseName}]");
        try
        {
            await ApplyAsync(connectionString, script => string.CompareOrdinal(script.Name, LegAccountsScript) < 0);

            await ExecuteAsync(connectionString, """
                ALTER TABLE [Accounts] NOCHECK CONSTRAINT [FK_Accounts_Users_CustomerId];

                INSERT INTO [Tenants] ([PublicId], [Name], [Code], [Created], [LastModified], [ReportingCurrency])
                VALUES (NEWID(), N'Backfill', N'backfill', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), 'NZD');
                DECLARE @tenant int = SCOPE_IDENTITY();

                INSERT INTO [Accounts] ([PublicId], [TenantId], [CustomerId], [Name], [Type], [Status], [Currency], [Created], [CreatedBy])
                VALUES (NEWID(), @tenant, 1, N'Bank', 0, 0, 'NZD', SYSDATETIMEOFFSET(), N'test');
                DECLARE @bank int = SCOPE_IDENTITY();
                INSERT INTO [Accounts] ([PublicId], [TenantId], [CustomerId], [Name], [Type], [Status], [Currency], [Created], [CreatedBy])
                VALUES (NEWID(), @tenant, 1, N'Brokerage', 2, 0, 'NZD', SYSDATETIMEOFFSET(), N'test');
                DECLARE @brokerage int = SCOPE_IDENTITY();

                INSERT INTO [Instruments] ([PublicId], [TenantId], [Symbol], [Name], [QuoteCurrency], [Created], [CreatedBy])
                VALUES (NEWID(), @tenant, N'VTI', N'Vanguard', 'USD', SYSDATETIMEOFFSET(), N'test');
                DECLARE @instrument int = SCOPE_IDENTITY();

                INSERT INTO [Transactions] ([PublicId], [TenantId], [AccountId], [Type], [BookedAt], [Created], [CreatedBy])
                VALUES (NEWID(), @tenant, @bank, 0, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'test');
                INSERT INTO [TransactionCashLegs] ([TransactionId], [Amount], [Currency], [Created], [CreatedBy])
                VALUES (SCOPE_IDENTITY(), 100, 'NZD', SYSDATETIMEOFFSET(), N'test');

                INSERT INTO [Transactions] ([PublicId], [TenantId], [AccountId], [Type], [BookedAt], [Created], [CreatedBy])
                VALUES (NEWID(), @tenant, @brokerage, 0, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'test');
                DECLARE @buy int = SCOPE_IDENTITY();
                INSERT INTO [TransactionCashLegs] ([TransactionId], [Amount], [Currency], [Created], [CreatedBy])
                VALUES (@buy, -50, 'NZD', SYSDATETIMEOFFSET(), N'test');
                INSERT INTO [TransactionSecurityLegs] ([TransactionId], [InstrumentId], [Quantity], [CostAmount], [CostCurrency], [Created], [CreatedBy])
                VALUES (@buy, @instrument, 2, 30, 'USD', SYSDATETIMEOFFSET(), N'test');
                """);

            var expected = await ReadAsync(connectionString, """
                SELECT 'cash', l.[Id], t.[AccountId] FROM [TransactionCashLegs] l JOIN [Transactions] t ON t.[Id] = l.[TransactionId]
                UNION ALL
                SELECT 'security', l.[Id], t.[AccountId] FROM [TransactionSecurityLegs] l JOIN [Transactions] t ON t.[Id] = l.[TransactionId]
                ORDER BY 1, 2
                """);

            await ApplyAsync(connectionString, _ => true);

            var actual = await ReadAsync(connectionString, """
                SELECT 'cash', [Id], [AccountId] FROM [TransactionCashLegs]
                UNION ALL
                SELECT 'security', [Id], [AccountId] FROM [TransactionSecurityLegs]
                ORDER BY 1, 2
                """);

            expected.Count.ShouldBe(3);
            actual.ShouldBe(expected);
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await ExecuteOnMasterAsync(
                $"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}]");
        }
    }

    private static async Task ApplyAsync(string connectionString, Func<SchemaScript, bool> include)
    {
        await using var db = OpenDb(connectionString);
        var applicator = new SchemaApplicator(
            new FilteredScriptSource(new FileSchemaScriptSource(SchemaDirectory()), include),
            new EfSchemaVersionStore(db),
            new EfSqlBatchExecutor(db));
        await applicator.ApplyAsync(CancellationToken.None);
    }

    private static async Task ExecuteOnMasterAsync(string sql)
    {
        var master = new SqlConnectionStringBuilder(SchemaDatabaseSetup.ConnectionString)
        {
            InitialCatalog = "master"
        }.ConnectionString;
        await ExecuteAsync(master, sql);
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<List<(string Kind, int LegId, int AccountId)>> ReadAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var rows = new List<(string Kind, int LegId, int AccountId)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2)));
        }

        return rows;
    }

    private static ApplicationDbContext OpenDb(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new ApplicationDbContext(options);
    }

    private static string SchemaDirectory() => Path.Combine(AppContext.BaseDirectory, "schema");

    private sealed class FilteredScriptSource(ISchemaScriptSource inner, Func<SchemaScript, bool> include)
        : ISchemaScriptSource
    {
        public IReadOnlyList<SchemaScript> GetScripts() => inner.GetScripts().Where(include).ToList();
    }
}
