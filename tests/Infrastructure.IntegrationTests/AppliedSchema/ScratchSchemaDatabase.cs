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
    /// Account, two Instruments, a Buy (cash + security leg), an FxConversion (two cash legs + FX detail),
    /// a Portfolio with one line (on the second Instrument) and a Report schedule.
    /// Variables are named after <paramref name="prefix"/>: <c>@{prefix}_tenant</c>, <c>_adviser</c>,
    /// <c>_customer</c>, <c>_brokerage</c>, <c>_bank</c>, <c>_instrument</c>, <c>_other</c>, <c>_buy</c>,
    /// <c>_fx</c>, <c>_portfolio</c> and <c>_schedule</c>. <paramref name="code"/> makes the tenant and
    /// identity rows unique. Set <paramref name="childTenantIds"/> to false for the schema before <c>0027</c>.
    /// </summary>
    public static string TenantRows(string prefix, string code, bool childTenantIds = true)
    {
        var t = $"@{prefix}_tenant";
        var tenantColumn = childTenantIds ? "[TenantId], " : "";
        var tenantValue = childTenantIds ? $"{t}, " : "";
        const string emptyJson = "{}";
        return $"""
            INSERT INTO [Tenants] ([PublicId], [Name], [Code], [Created], [LastModified], [ReportingCurrency])
            VALUES (NEWID(), N'Tenant {code}', N'{code}', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), 'NZD');
            DECLARE {t} int = SCOPE_IDENTITY();

            INSERT INTO [AspNetUsers] ([Id], [EmailConfirmed], [PhoneNumberConfirmed], [TwoFactorEnabled], [LockoutEnabled], [AccessFailedCount], [TenantId])
            VALUES (N'{code}-adviser', 1, 0, 0, 0, 0, {t}), (N'{code}-customer', 1, 0, 0, 0, 0, {t});
            INSERT INTO [Users] ([PublicId], [TenantId], [Name], [Email], [Role], [Status], [AdviserId], [IdentityUserId], [Created], [LastModified])
            VALUES (NEWID(), {t}, N'Adviser', N'adviser@{code}.example', 2, 1, NULL, N'{code}-adviser', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
            DECLARE @{prefix}_adviser int = SCOPE_IDENTITY();
            INSERT INTO [Users] ([PublicId], [TenantId], [Name], [Email], [Role], [Status], [AdviserId], [IdentityUserId], [Created], [LastModified])
            VALUES (NEWID(), {t}, N'Customer', N'customer@{code}.example', 3, 1, @{prefix}_adviser, N'{code}-customer', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
            DECLARE @{prefix}_customer int = SCOPE_IDENTITY();

            INSERT INTO [Accounts] ([PublicId], [TenantId], [CustomerId], [Name], [Type], [Status], [Currency], [Created], [CreatedBy])
            VALUES (NEWID(), {t}, @{prefix}_customer, N'Brokerage', 2, 0, 'NZD', SYSDATETIMEOFFSET(), N'test');
            DECLARE @{prefix}_brokerage int = SCOPE_IDENTITY();
            INSERT INTO [Accounts] ([PublicId], [TenantId], [CustomerId], [Name], [Type], [Status], [Currency], [Created], [CreatedBy])
            VALUES (NEWID(), {t}, @{prefix}_customer, N'USD Bank', 0, 0, 'USD', SYSDATETIMEOFFSET(), N'test');
            DECLARE @{prefix}_bank int = SCOPE_IDENTITY();

            INSERT INTO [Instruments] ([PublicId], [TenantId], [Symbol], [Name], [QuoteCurrency], [Created], [CreatedBy])
            VALUES (NEWID(), {t}, N'VTI', N'Vanguard', 'USD', SYSDATETIMEOFFSET(), N'test');
            DECLARE @{prefix}_instrument int = SCOPE_IDENTITY();
            INSERT INTO [Instruments] ([PublicId], [TenantId], [Symbol], [Name], [QuoteCurrency], [Created], [CreatedBy])
            VALUES (NEWID(), {t}, N'BND', N'Bonds', 'USD', SYSDATETIMEOFFSET(), N'test');
            DECLARE @{prefix}_other int = SCOPE_IDENTITY();

            INSERT INTO [Transactions] ([PublicId], [TenantId], [Type], [BookedAt], [Created], [CreatedBy])
            VALUES (NEWID(), {t}, 7, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'test');
            DECLARE @{prefix}_buy int = SCOPE_IDENTITY();
            INSERT INTO [TransactionCashLegs] ({tenantColumn}[TransactionId], [AccountId], [Amount], [Currency], [Created], [CreatedBy])
            VALUES ({tenantValue}@{prefix}_buy, @{prefix}_brokerage, -20, 'NZD', SYSDATETIMEOFFSET(), N'test');
            INSERT INTO [TransactionSecurityLegs] ({tenantColumn}[TransactionId], [AccountId], [InstrumentId], [Quantity], [CostAmount], [CostCurrency], [Created], [CreatedBy])
            VALUES ({tenantValue}@{prefix}_buy, @{prefix}_brokerage, @{prefix}_instrument, 1, 10, 'USD', SYSDATETIMEOFFSET(), N'test');

            INSERT INTO [Transactions] ([PublicId], [TenantId], [Type], [BookedAt], [Created], [CreatedBy])
            VALUES (NEWID(), {t}, 16, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'test');
            DECLARE @{prefix}_fx int = SCOPE_IDENTITY();
            INSERT INTO [TransactionCashLegs] ({tenantColumn}[TransactionId], [AccountId], [Amount], [Currency], [Created], [CreatedBy])
            VALUES ({tenantValue}@{prefix}_fx, @{prefix}_brokerage, -100, 'NZD', SYSDATETIMEOFFSET(), N'test'),
                   ({tenantValue}@{prefix}_fx, @{prefix}_bank, 50, 'USD', SYSDATETIMEOFFSET(), N'test');
            INSERT INTO [TransactionFxDetails] ({tenantColumn}[TransactionId], [BaseCurrency], [QuoteCurrency], [Rate], [RateAsOfDate], [RateSource])
            VALUES ({tenantValue}@{prefix}_fx, 'NZD', 'USD', 0.5, '2026-11-12', N'Frankfurter');

            INSERT INTO [Portfolios] ([PublicId], [TenantId], [Name], [BaseCurrency], [IsActive], [Created], [CreatedBy])
            VALUES (NEWID(), {t}, N'Balanced', 'NZD', 1, SYSDATETIMEOFFSET(), N'test');
            DECLARE @{prefix}_portfolio int = SCOPE_IDENTITY();
            INSERT INTO [PortfolioLines] ({tenantColumn}[PortfolioId], [InstrumentId], [TargetWeight], [SortOrder])
            VALUES ({tenantValue}@{prefix}_portfolio, @{prefix}_other, 100, 0);

            INSERT INTO [ReportSchedules] ([PublicId], [ReportId], [TenantId], [ParametersJson], [IsActive], [Cron], [Created], [CreatedBy])
            SELECT NEWID(), [Id], {t}, N'{emptyJson}', 0, N'0 8 * * 1-5', SYSDATETIMEOFFSET(), N'test'
            FROM [Reports] WHERE [Code] = N'adviser-balances';
            DECLARE @{prefix}_schedule int = SCOPE_IDENTITY();

            """;
    }

    /// <summary><see cref="TenantRows"/> for the schema after <c>0026</c>, before <c>0027</c>.</summary>
    public static string TenantRowsBefore0027(string prefix) => TenantRows(prefix, prefix, childTenantIds: false);

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
