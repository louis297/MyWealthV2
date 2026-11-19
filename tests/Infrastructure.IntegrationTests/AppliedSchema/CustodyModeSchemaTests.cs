using Microsoft.Data.SqlClient;
using MyWealthV2.Schema;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

/// <summary>Account custody mode §6: <c>Accounts.CustodyMode</c> (script 0031).</summary>
public class CustodyModeSchemaTests
{
    private const string CustodyModeScript = "0031_accounts_custody_mode.sql";

    private const string InsertAccount = """
        INSERT INTO [Accounts] ([PublicId], [TenantId], [CustomerId], [Name], [Type], [CustodyMode], [Status], [Currency], [Created], [CreatedBy])
        VALUES (NEWID(), @a_tenant, @a_customer, N'New', 0, {0}, 0, 'NZD', SYSDATETIMEOFFSET(), N'test');
        SELECT [CustodyMode] FROM [Accounts] WHERE [Id] = SCOPE_IDENTITY();
        """;

    [Test]
    public void SchemaDirectory_Includes0031CustodyModeScript()
    {
        new FileSchemaScriptSource(ScratchSchemaDatabase.SchemaDirectory()).GetScripts()
            .Select(script => script.Name)
            .ShouldContain(CustodyModeScript);
    }

    [Test]
    public async Task Apply_CustodyModeIsIntNotNullWithoutDefault()
    {
        var rows = await ScratchSchemaDatabase.ReadAsync(SchemaDatabaseSetup.ConnectionString, """
            SELECT DATA_TYPE, IS_NULLABLE, COLUMN_DEFAULT
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_NAME = N'Accounts' AND COLUMN_NAME = N'CustodyMode'
            """);

        rows.Count.ShouldBe(1);
        rows[0][0].ShouldBe("int");
        rows[0][1].ShouldBe("NO");
        rows[0][2].ShouldBe(DBNull.Value);
    }

    [Test]
    public async Task Apply_AddsCustodyModeCheck()
    {
        var rows = await ScratchSchemaDatabase.ReadAsync(SchemaDatabaseSetup.ConnectionString, """
            SELECT cc.definition
            FROM sys.check_constraints cc
            WHERE cc.parent_object_id = OBJECT_ID(N'Accounts') AND cc.name = N'CK_Accounts_CustodyMode'
            """);

        rows.Count.ShouldBe(1);
        var definition = (string)rows[0][0];
        definition.ShouldContain("[CustodyMode]", Case.Insensitive);
        definition.ShouldContain("0");
        definition.ShouldContain("1");
    }

    [TestCase(0)]
    [TestCase(1)]
    public async Task Insert_KnownCustodyMode_IsAccepted(int custodyMode)
    {
        var stored = await RunAsync(string.Format(InsertAccount, custodyMode));

        stored.ShouldBe(custodyMode);
    }

    [TestCase(-1)]
    [TestCase(2)]
    public async Task Insert_UnknownCustodyMode_IsRejected(int custodyMode)
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync(string.Format(InsertAccount, custodyMode)));

        exception.Number.ShouldBe(547);
    }

    [Test]
    public async Task Insert_WithoutCustodyMode_IsRejected()
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync("""
            INSERT INTO [Accounts] ([PublicId], [TenantId], [CustomerId], [Name], [Type], [Status], [Currency], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @a_customer, N'New', 0, 0, 'NZD', SYSDATETIMEOFFSET(), N'test');
            """));

        // 515: cannot insert NULL into a NOT NULL column that has no default.
        exception.Number.ShouldBe(515);
    }

    [Test]
    public async Task Apply0031_ExistingAccountsBecomeExternal()
    {
        await using var db = await ScratchSchemaDatabase.CreateAsync("CustodyModeBackfill");
        await db.ApplyBeforeAsync(CustodyModeScript);
        await db.ExecuteAsync(ScratchSchemaDatabase.TenantRows("a", Guid.NewGuid().ToString("N")[..12], custodyMode: false, transactionSource: false));
        var before = await db.ReadAsync("SELECT [Id] FROM [Accounts] ORDER BY [Id]");

        await db.ApplyAllAsync();

        var after = await db.ReadAsync("SELECT [Id], [CustodyMode] FROM [Accounts] ORDER BY [Id]");
        before.Count.ShouldBe(2);
        after.Select(row => (int)row[0]).ShouldBe(before.Select(row => (int)row[0]));
        after.ShouldAllBe(row => (int)row[1] == 0);
        (await db.ReadAsync($"SELECT 1 FROM [SchemaVersions] WHERE [ScriptName] = N'{CustodyModeScript}'"))
            .Count.ShouldBe(1);
    }

    /// <summary>Runs <paramref name="statement"/> after one tenant's rows, then rolls back.</summary>
    private static async Task<object?> RunAsync(string statement)
    {
        await using var connection = new SqlConnection(SchemaDatabaseSetup.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = ScratchSchemaDatabase.TenantRows("a", Guid.NewGuid().ToString("N")[..12]) + statement;
            return await command.ExecuteScalarAsync();
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }
}
