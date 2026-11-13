using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Data.Schema;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

/// <summary>
/// A throw-away database on the schema test server, for tests that apply only some schema scripts,
/// insert rows by hand, and then apply the rest.
/// </summary>
internal sealed class ScratchSchemaDatabase : IAsyncDisposable
{
    private ScratchSchemaDatabase(string name, string connectionString)
    {
        Name = name;
        ConnectionString = connectionString;
    }

    public string Name { get; }

    public string ConnectionString { get; }

    public static async Task<ScratchSchemaDatabase> CreateAsync(string prefix)
    {
        var name = $"{prefix}_{Guid.NewGuid():N}";
        var connectionString = new SqlConnectionStringBuilder(SchemaDatabaseSetup.ConnectionString)
        {
            InitialCatalog = name
        }.ConnectionString;
        await ExecuteOnMasterAsync($"CREATE DATABASE [{name}]");
        return new ScratchSchemaDatabase(name, connectionString);
    }

    /// <summary>Applies every script whose name sorts before <paramref name="scriptName"/>.</summary>
    public Task ApplyBeforeAsync(string scriptName) =>
        ApplyAsync(script => string.CompareOrdinal(script.Name, scriptName) < 0);

    public Task ApplyAllAsync() => ApplyAsync(_ => true);

    public async Task ExecuteAsync(string sql)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    public Task<List<object[]>> ReadAsync(string sql) => ReadAsync(ConnectionString, sql);

    public static async Task<List<object[]>> ReadAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var rows = new List<object[]>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var row = new object[reader.FieldCount];
            reader.GetValues(row);
            rows.Add(row);
        }

        return rows;
    }

    public async ValueTask DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        await ExecuteOnMasterAsync(
            $"ALTER DATABASE [{Name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{Name}]");
    }

    /// <summary>
    /// SQL that inserts one tenant with an Adviser, a Customer, an NZD brokerage Account, a USD bank
    /// Account, an Instrument, a Buy (cash + security leg), an FxConversion (two cash legs + FX detail)
    /// and a Portfolio with one line. Valid for the schema after <c>0026</c>, before <c>0027</c>.
    /// Declares <c>@{code}_tenant</c>, <c>@{code}_customer</c>, <c>@{code}_brokerage</c>,
    /// <c>@{code}_bank</c>, <c>@{code}_instrument</c>, <c>@{code}_buy</c> and <c>@{code}_portfolio</c>.
    /// </summary>
    public static string TenantRowsBefore0027(string code) => $"""
        INSERT INTO [Tenants] ([PublicId], [Name], [Code], [Created], [LastModified], [ReportingCurrency])
        VALUES (NEWID(), N'Tenant {code}', N'{code}', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), 'NZD');
        DECLARE @{code}_tenant int = SCOPE_IDENTITY();

        INSERT INTO [AspNetUsers] ([Id], [EmailConfirmed], [PhoneNumberConfirmed], [TwoFactorEnabled], [LockoutEnabled], [AccessFailedCount], [TenantId])
        VALUES (N'{code}-adviser', 1, 0, 0, 0, 0, @{code}_tenant), (N'{code}-customer', 1, 0, 0, 0, 0, @{code}_tenant);
        INSERT INTO [Users] ([PublicId], [TenantId], [Name], [Email], [Role], [Status], [AdviserId], [IdentityUserId], [Created], [LastModified])
        VALUES (NEWID(), @{code}_tenant, N'Adviser', N'adviser@{code}.example', 2, 1, NULL, N'{code}-adviser', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
        DECLARE @{code}_adviser int = SCOPE_IDENTITY();
        INSERT INTO [Users] ([PublicId], [TenantId], [Name], [Email], [Role], [Status], [AdviserId], [IdentityUserId], [Created], [LastModified])
        VALUES (NEWID(), @{code}_tenant, N'Customer', N'customer@{code}.example', 3, 1, @{code}_adviser, N'{code}-customer', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
        DECLARE @{code}_customer int = SCOPE_IDENTITY();

        INSERT INTO [Accounts] ([PublicId], [TenantId], [CustomerId], [Name], [Type], [Status], [Currency], [Created], [CreatedBy])
        VALUES (NEWID(), @{code}_tenant, @{code}_customer, N'Brokerage', 2, 0, 'NZD', SYSDATETIMEOFFSET(), N'test');
        DECLARE @{code}_brokerage int = SCOPE_IDENTITY();
        INSERT INTO [Accounts] ([PublicId], [TenantId], [CustomerId], [Name], [Type], [Status], [Currency], [Created], [CreatedBy])
        VALUES (NEWID(), @{code}_tenant, @{code}_customer, N'USD Bank', 0, 0, 'USD', SYSDATETIMEOFFSET(), N'test');
        DECLARE @{code}_bank int = SCOPE_IDENTITY();

        INSERT INTO [Instruments] ([PublicId], [TenantId], [Symbol], [Name], [QuoteCurrency], [Created], [CreatedBy])
        VALUES (NEWID(), @{code}_tenant, N'VTI', N'Vanguard', 'USD', SYSDATETIMEOFFSET(), N'test');
        DECLARE @{code}_instrument int = SCOPE_IDENTITY();

        INSERT INTO [Transactions] ([PublicId], [TenantId], [Type], [BookedAt], [Created], [CreatedBy])
        VALUES (NEWID(), @{code}_tenant, 7, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'test');
        DECLARE @{code}_buy int = SCOPE_IDENTITY();
        INSERT INTO [TransactionCashLegs] ([TransactionId], [AccountId], [Amount], [Currency], [Created], [CreatedBy])
        VALUES (@{code}_buy, @{code}_brokerage, -20, 'NZD', SYSDATETIMEOFFSET(), N'test');
        INSERT INTO [TransactionSecurityLegs] ([TransactionId], [AccountId], [InstrumentId], [Quantity], [CostAmount], [CostCurrency], [Created], [CreatedBy])
        VALUES (@{code}_buy, @{code}_brokerage, @{code}_instrument, 1, 10, 'USD', SYSDATETIMEOFFSET(), N'test');

        INSERT INTO [Transactions] ([PublicId], [TenantId], [Type], [BookedAt], [Created], [CreatedBy])
        VALUES (NEWID(), @{code}_tenant, 16, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'test');
        DECLARE @{code}_fx int = SCOPE_IDENTITY();
        INSERT INTO [TransactionCashLegs] ([TransactionId], [AccountId], [Amount], [Currency], [Created], [CreatedBy])
        VALUES (@{code}_fx, @{code}_brokerage, -100, 'NZD', SYSDATETIMEOFFSET(), N'test'),
               (@{code}_fx, @{code}_bank, 50, 'USD', SYSDATETIMEOFFSET(), N'test');
        INSERT INTO [TransactionFxDetails] ([TransactionId], [BaseCurrency], [QuoteCurrency], [Rate], [RateAsOfDate], [RateSource])
        VALUES (@{code}_fx, 'NZD', 'USD', 0.5, '2026-11-12', N'Frankfurter');

        INSERT INTO [Portfolios] ([PublicId], [TenantId], [Name], [BaseCurrency], [IsActive], [Created], [CreatedBy])
        VALUES (NEWID(), @{code}_tenant, N'Balanced', 'NZD', 1, SYSDATETIMEOFFSET(), N'test');
        DECLARE @{code}_portfolio int = SCOPE_IDENTITY();
        INSERT INTO [PortfolioLines] ([PortfolioId], [InstrumentId], [TargetWeight], [SortOrder])
        VALUES (@{code}_portfolio, @{code}_instrument, 100, 0);
        """;

    private async Task ApplyAsync(Func<SchemaScript, bool> include)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        await using var db = new ApplicationDbContext(options);
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
        await using var connection = new SqlConnection(master);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    internal static string SchemaDirectory() => Path.Combine(AppContext.BaseDirectory, "schema");

    private sealed class FilteredScriptSource(ISchemaScriptSource inner, Func<SchemaScript, bool> include)
        : ISchemaScriptSource
    {
        public IReadOnlyList<SchemaScript> GetScripts() => inner.GetScripts().Where(include).ToList();
    }
}
