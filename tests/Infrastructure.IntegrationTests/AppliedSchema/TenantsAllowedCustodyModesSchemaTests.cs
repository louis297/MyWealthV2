using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Data.Schema;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

/// <summary>Tenant custody modes §6: <c>Tenants.AllowsExternalAccounts</c> and <c>AllowsCustodialAccounts</c> (script 0033).</summary>
public class TenantsAllowedCustodyModesSchemaTests
{
    private const string AllowedCustodyModesScript = "0033_tenants_allowed_custody_modes.sql";

    private const string InsertTenantNamingNeither = """
        INSERT INTO [Tenants] ([PublicId], [Name], [Code], [Created], [LastModified], [ReportingCurrency])
        VALUES (NEWID(), N'Tenant {0}', N'{0}', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), 'NZD');
        SELECT CAST([AllowsExternalAccounts] AS int) * 10 + CAST([AllowsCustodialAccounts] AS int)
        FROM [Tenants] WHERE [Id] = SCOPE_IDENTITY();
        """;

    [Test]
    public void SchemaDirectory_Includes0033Script()
    {
        new FileSchemaScriptSource(ScratchSchemaDatabase.SchemaDirectory()).GetScripts()
            .Select(script => script.Name)
            .ShouldContain(AllowedCustodyModesScript);
    }

    [TestCase("AllowsExternalAccounts")]
    [TestCase("AllowsCustodialAccounts")]
    public async Task Apply_AddsBitNotNullColumnWithNamedDefaultOfOne(string column)
    {
        var rows = await ScratchSchemaDatabase.ReadAsync(SchemaDatabaseSetup.ConnectionString, $"""
            SELECT c.DATA_TYPE, c.IS_NULLABLE, dc.name, dc.definition
            FROM INFORMATION_SCHEMA.COLUMNS c
            LEFT JOIN sys.default_constraints dc
                ON dc.parent_object_id = OBJECT_ID(N'Tenants')
                AND dc.parent_column_id = COLUMNPROPERTY(OBJECT_ID(N'Tenants'), c.COLUMN_NAME, 'ColumnId')
            WHERE c.TABLE_NAME = N'Tenants' AND c.COLUMN_NAME = N'{column}'
            """);

        rows.Count.ShouldBe(1);
        rows[0][0].ShouldBe("bit");
        rows[0][1].ShouldBe("NO");
        rows[0][2].ShouldBe($"DF_Tenants_{column}");
        ((string)rows[0][3]).ShouldContain("1");
    }

    [Test]
    public async Task Apply_AddsAtLeastOneCheck()
    {
        var rows = await ScratchSchemaDatabase.ReadAsync(SchemaDatabaseSetup.ConnectionString, """
            SELECT cc.definition
            FROM sys.check_constraints cc
            WHERE cc.parent_object_id = OBJECT_ID(N'Tenants') AND cc.name = N'CK_Tenants_AllowedCustodyModes'
            """);

        rows.Count.ShouldBe(1);
        var definition = (string)rows[0][0];
        definition.ShouldContain("[AllowsExternalAccounts]", Case.Insensitive);
        definition.ShouldContain("[AllowsCustodialAccounts]", Case.Insensitive);
    }

    [Test]
    public async Task Insert_NamingNeitherColumn_AllowsBoth()
    {
        var stored = await RunAsync(string.Format(InsertTenantNamingNeither, Code()));

        stored.ShouldBe(11);
    }

    [Test]
    public async Task Insert_WithBothColumnsZero_IsRejected()
    {
        var code = Code();
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            INSERT INTO [Tenants] ([PublicId], [Name], [Code], [Created], [LastModified], [ReportingCurrency], [AllowsExternalAccounts], [AllowsCustodialAccounts])
            VALUES (NEWID(), N'Tenant {code}', N'{code}', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), 'NZD', 0, 0);
            """));

        exception.Number.ShouldBe(547);
    }

    [Test]
    public async Task Update_ToBothColumnsZero_IsRejected()
    {
        var code = Code();
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            INSERT INTO [Tenants] ([PublicId], [Name], [Code], [Created], [LastModified], [ReportingCurrency])
            VALUES (NEWID(), N'Tenant {code}', N'{code}', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), 'NZD');
            UPDATE [Tenants] SET [AllowsExternalAccounts] = 0, [AllowsCustodialAccounts] = 0 WHERE [Id] = SCOPE_IDENTITY();
            """));

        exception.Number.ShouldBe(547);
    }

    [Test]
    public async Task Apply0033_ExistingTenantsAllowBoth()
    {
        await using var db = await ScratchSchemaDatabase.CreateAsync("AllowedCustodyModesBackfill");
        await db.ApplyBeforeAsync(AllowedCustodyModesScript);
        await db.ExecuteAsync(ScratchSchemaDatabase.TenantRows("a", Code(), transactionSource: false));
        var before = await db.ReadAsync("SELECT [Id] FROM [Tenants] ORDER BY [Id]");

        await db.ApplyAllAsync();

        var after = await db.ReadAsync(
            "SELECT [Id], [AllowsExternalAccounts], [AllowsCustodialAccounts] FROM [Tenants] ORDER BY [Id]");
        before.Count.ShouldBe(1);
        after.Select(row => (int)row[0]).ShouldBe(before.Select(row => (int)row[0]));
        after.ShouldAllBe(row => (bool)row[1] && (bool)row[2]);
        (await db.ReadAsync($"SELECT 1 FROM [SchemaVersions] WHERE [ScriptName] = N'{AllowedCustodyModesScript}'"))
            .Count.ShouldBe(1);
    }

    [Test]
    public async Task EfRoundTrip_KeepsANarrowedSetting()
    {
        await using var db = await ScratchSchemaDatabase.CreateAsync("AllowedCustodyModesEf");
        await db.ApplyAllAsync();
        var tenant = Tenant.Create("Round Trip", Code(), Currency.Create("NZD", "New Zealand Dollar", 2));
        tenant.SetAllowedCustodyModes([CustodyMode.Custodial]);

        await using (var context = OpenDb(db.ConnectionString))
        {
            context.Tenants.Add(tenant);
            await context.SaveChangesAsync();
        }

        await using (var context = OpenDb(db.ConnectionString))
        {
            var reloaded = await context.Tenants.AsNoTracking().SingleAsync(row => row.PublicId == tenant.PublicId);
            reloaded.AllowedCustodyModes.ShouldBe([CustodyMode.Custodial]);
        }
    }

    private static string Code() => Guid.NewGuid().ToString("N")[..12];

    private static ApplicationDbContext OpenDb(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new ApplicationDbContext(options);
    }

    /// <summary>Runs <paramref name="statement"/> on the schema test database, then rolls back.</summary>
    private static async Task<object?> RunAsync(string statement)
    {
        await using var connection = new SqlConnection(SchemaDatabaseSetup.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = statement;
            return await command.ExecuteScalarAsync();
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }
}
