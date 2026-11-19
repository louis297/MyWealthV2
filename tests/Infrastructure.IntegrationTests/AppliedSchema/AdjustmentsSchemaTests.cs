using Microsoft.Data.SqlClient;
using MyWealthV2.Schema;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

/// <summary>Adjustments §6: <c>CK_Transactions_Type</c> 0–17 and <c>TransactionAdjustmentDetails</c> (script 0037).</summary>
public class AdjustmentsSchemaTests
{
    private const string AdjustmentsScript = "0037_adjustments.sql";

    private const string InsertAdjustment = """
        INSERT INTO [Transactions] ([PublicId], [TenantId], [Type], [SourceKind], [BookedAt], [Created], [CreatedBy])
        VALUES (NEWID(), @a_tenant, 17, 0, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'test');
        DECLARE @adjustment int = SCOPE_IDENTITY();

        """;

    [Test]
    public void SchemaDirectory_Includes0037AdjustmentsScript()
    {
        new FileSchemaScriptSource(ScratchSchemaDatabase.SchemaDirectory()).GetScripts()
            .Select(script => script.Name)
            .ShouldContain(AdjustmentsScript);
    }

    [Test]
    public async Task Apply_AddsTransactionAdjustmentDetailsWithThreeIntColumns()
    {
        var rows = await ScratchSchemaDatabase.ReadAsync(SchemaDatabaseSetup.ConnectionString, """
            SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_NAME = N'TransactionAdjustmentDetails'
            ORDER BY COLUMN_NAME
            """);

        rows.Select(row => $"{row[0]}|{row[1]}|{row[2]}").ShouldBe(
        [
            "Reason|int|NO",
            "TenantId|int|NO",
            "TransactionId|int|NO"
        ]);
    }

    [Test]
    public async Task Apply_TransactionIdIsThePrimaryKey_AndTenantIdIsIndexed()
    {
        var primaryKey = await ScratchSchemaDatabase.ReadAsync(SchemaDatabaseSetup.ConnectionString, """
            SELECT c.[name]
            FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.[object_id] = i.[object_id] AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.[object_id] = ic.[object_id] AND c.column_id = ic.column_id
            WHERE i.[object_id] = OBJECT_ID(N'TransactionAdjustmentDetails') AND i.is_primary_key = 1
            """);
        var tenantIndex = await ScratchSchemaDatabase.ReadAsync(SchemaDatabaseSetup.ConnectionString, """
            SELECT i.[name]
            FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.[object_id] = i.[object_id] AND ic.index_id = i.index_id AND ic.key_ordinal = 1
            JOIN sys.columns c ON c.[object_id] = ic.[object_id] AND c.column_id = ic.column_id
            WHERE i.[object_id] = OBJECT_ID(N'TransactionAdjustmentDetails') AND i.is_primary_key = 0
              AND c.[name] = N'TenantId'
            """);

        primaryKey.Select(row => (string)row[0]).ShouldBe(["TransactionId"]);
        tenantIndex.ShouldNotBeEmpty();
    }

    [Test]
    public async Task Insert_TypeSeventeen_IsAccepted()
    {
        var type = await RunAsync(InsertAdjustment + "SELECT [Type] FROM [Transactions] WHERE [Id] = @adjustment;");

        type.ShouldBe(17);
    }

    [Test]
    public async Task Insert_TypeEighteen_IsRejected()
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync("""
            INSERT INTO [Transactions] ([PublicId], [TenantId], [Type], [SourceKind], [BookedAt], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, 18, 0, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'test');
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain("CK_Transactions_Type");
    }

    [TestCase(0)]
    [TestCase(3)]
    public async Task Insert_DetailWithAKnownReason_IsAccepted(int reason)
    {
        var stored = await RunAsync(InsertAdjustment + $"""
            INSERT INTO [TransactionAdjustmentDetails] ([TransactionId], [TenantId], [Reason])
            VALUES (@adjustment, @a_tenant, {reason});
            SELECT [Reason] FROM [TransactionAdjustmentDetails] WHERE [TransactionId] = @adjustment;
            """);

        stored.ShouldBe(reason);
    }

    [TestCase(4)]
    [TestCase(-1)]
    public async Task Insert_DetailWithAnUnknownReason_IsRejected(int reason)
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync(InsertAdjustment + $"""
            INSERT INTO [TransactionAdjustmentDetails] ([TransactionId], [TenantId], [Reason])
            VALUES (@adjustment, @a_tenant, {reason});
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain("CK_TransactionAdjustmentDetails_Reason");
    }

    [Test]
    public async Task Insert_DetailWhoseTenantDiffersFromItsTransaction_IsRejected()
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync(
            InsertAdjustment
            + ScratchSchemaDatabase.TenantRows("b", Guid.NewGuid().ToString("N")[..12])
            + """
              INSERT INTO [TransactionAdjustmentDetails] ([TransactionId], [TenantId], [Reason])
              VALUES (@adjustment, @b_tenant, 0);
              """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain("FK_TransactionAdjustmentDetails_Transactions_TenantId_TransactionId");
    }

    [Test]
    public async Task Apply0037_LeavesExistingRowsUnchanged()
    {
        await using var db = await ScratchSchemaDatabase.CreateAsync("Adjustments");
        await db.ApplyBeforeAsync(AdjustmentsScript);
        await db.ExecuteAsync(ScratchSchemaDatabase.TenantRows("a", Guid.NewGuid().ToString("N")[..12]));
        var transactionsBefore = Flatten(await db.ReadAsync(TransactionsSql));
        var cashBefore = Flatten(await db.ReadAsync(CashLegsSql));
        var securityBefore = Flatten(await db.ReadAsync(SecurityLegsSql));

        await db.ApplyAllAsync();

        Flatten(await db.ReadAsync(TransactionsSql)).ShouldBe(transactionsBefore);
        Flatten(await db.ReadAsync(CashLegsSql)).ShouldBe(cashBefore);
        Flatten(await db.ReadAsync(SecurityLegsSql)).ShouldBe(securityBefore);
        (await db.ReadAsync("SELECT 1 FROM [TransactionAdjustmentDetails]")).ShouldBeEmpty();
        (await db.ReadAsync($"SELECT 1 FROM [SchemaVersions] WHERE [ScriptName] = N'{AdjustmentsScript}'"))
            .Count.ShouldBe(1);
    }

    private const string TransactionsSql =
        "SELECT [Id], [Type], [SourceKind], [TenantId] FROM [Transactions] ORDER BY [Id]";

    private const string CashLegsSql =
        "SELECT [TransactionId], [AccountId], [Amount], [Currency] FROM [TransactionCashLegs] ORDER BY [Id]";

    private const string SecurityLegsSql =
        "SELECT [TransactionId], [AccountId], [InstrumentId], [Quantity], [CostAmount] FROM [TransactionSecurityLegs] ORDER BY [Id]";

    private static List<string> Flatten(List<object[]> rows) =>
        rows.Select(row => string.Join("|", row.Select(value => Convert.ToString(value)))).ToList();

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
