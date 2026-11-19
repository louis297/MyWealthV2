using Microsoft.Data.SqlClient;
using MyWealthV2.Infrastructure.Data.Schema;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

/// <summary>Transaction source §6: <c>Transactions.SourceKind</c> and <c>SourcePublicId</c> (script 0035).</summary>
public class TransactionSourceSchemaTests
{
    private const string TransactionSourceScript = "0035_transaction_source.sql";

    private const string BuyHandle = "0f8fad5bd9cb469fa16570867728950e";
    private const string SellHandle = "7c9e6679742540de944be07fc1f90ae7";

    private static readonly string[] SeedReferences =
    [
        "TestSeed-DemoCustodial-Funding",
        "TestSeed-DemoCustodial-BuyVti",
        "TestSeed-DemoCustodial-BuyAapl",
        "TestSeed-DemoBrokerage-OpeningHoldings",
        "TestSeed-DemoBrokerage-Funding",
        "TestSeed-DemoBrokerage-Buy",
        "TestSeed-DemoBrokerage-StockIn",
        "TestSeed-DemoBrokerage-Split",
        "TestSeed-DemoEverydayBank-OpeningTransfer",
        "Smoke-Cash-TransferIn"
    ];

    private const string InsertTransaction = """
        INSERT INTO [Transactions] ([PublicId], [TenantId], [Type], [SourceKind], [SourcePublicId], [BookedAt], [Created], [CreatedBy])
        VALUES (NEWID(), @a_tenant, 0, {0}, {1}, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'test');
        SELECT [SourceKind] FROM [Transactions] WHERE [Id] = SCOPE_IDENTITY();
        """;

    [Test]
    public void SchemaDirectory_Includes0035TransactionSourceScript()
    {
        new FileSchemaScriptSource(ScratchSchemaDatabase.SchemaDirectory()).GetScripts()
            .Select(script => script.Name)
            .ShouldContain(TransactionSourceScript);
    }

    [Test]
    public async Task Apply_SourceKindIsIntNotNullWithoutDefault_AndSourcePublicIdIsNullableGuid()
    {
        var rows = await ScratchSchemaDatabase.ReadAsync(SchemaDatabaseSetup.ConnectionString, """
            SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE, COLUMN_DEFAULT
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_NAME = N'Transactions' AND COLUMN_NAME IN (N'SourceKind', N'SourcePublicId')
            ORDER BY COLUMN_NAME
            """);

        rows.Count.ShouldBe(2);
        rows[0][0].ShouldBe("SourceKind");
        rows[0][1].ShouldBe("int");
        rows[0][2].ShouldBe("NO");
        rows[0][3].ShouldBe(DBNull.Value);
        rows[1][0].ShouldBe("SourcePublicId");
        rows[1][1].ShouldBe("uniqueidentifier");
        rows[1][2].ShouldBe("YES");
    }

    [Test]
    public async Task Apply_AddsBothSourceChecks()
    {
        var rows = await ScratchSchemaDatabase.ReadAsync(SchemaDatabaseSetup.ConnectionString, """
            SELECT cc.name
            FROM sys.check_constraints cc
            WHERE cc.parent_object_id = OBJECT_ID(N'Transactions')
              AND cc.name IN (N'CK_Transactions_SourceKind', N'CK_Transactions_SourcePublicId')
            ORDER BY cc.name
            """);

        rows.Select(row => (string)row[0]).ShouldBe(["CK_Transactions_SourceKind", "CK_Transactions_SourcePublicId"]);
    }

    [TestCase(0, "NULL")]
    [TestCase(1, "NULL")]
    [TestCase(2, "NEWID()")]
    public async Task Insert_ValidSource_IsAccepted(int kind, string publicId)
    {
        var stored = await RunAsync(string.Format(InsertTransaction, kind, publicId));

        stored.ShouldBe(kind);
    }

    [TestCase(2, "NULL")]
    [TestCase(0, "NEWID()")]
    [TestCase(1, "NEWID()")]
    [TestCase(3, "NULL")]
    [TestCase(-1, "NULL")]
    public async Task Insert_InvalidSource_IsRejected(int kind, string publicId)
    {
        var exception = await Should.ThrowAsync<SqlException>(
            () => RunAsync(string.Format(InsertTransaction, kind, publicId)));

        exception.Number.ShouldBe(547);
    }

    [Test]
    public async Task Insert_WithoutSourceKind_IsRejected()
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync("""
            INSERT INTO [Transactions] ([PublicId], [TenantId], [Type], [BookedAt], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, 0, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'test');
            """));

        // 515: cannot insert NULL into a NOT NULL column that has no default.
        exception.Number.ShouldBe(515);
    }

    [Test]
    public async Task Apply0035_BackFillsRebalanceSeedAndManualRows()
    {
        await using var db = await ScratchSchemaDatabase.CreateAsync("TransactionSourceBackfill");
        await db.ApplyBeforeAsync(TransactionSourceScript);
        await db.ExecuteAsync(ScratchSchemaDatabase.TenantRows(
                                  "a", Guid.NewGuid().ToString("N")[..12], transactionSource: false)
                              + BackfillRows());
        var cashBefore = Flatten(await db.ReadAsync(LegsSql));
        var securityBefore = Flatten(await db.ReadAsync(SecurityLegsSql));

        await db.ApplyAllAsync();

        var rows = (await db.ReadAsync(
                "SELECT [Memo], [SourceKind], [SourcePublicId], [Reference] FROM [Transactions] WHERE [Memo] IS NOT NULL"))
            .ToDictionary(row => (string)row[0]);

        rows["rb-buy"][1].ShouldBe(2);
        rows["rb-buy"][2].ShouldBe(Guid.Parse(BuyHandle));
        rows["rb-buy"][3].ShouldBe(DBNull.Value);
        rows["rb-sell"][1].ShouldBe(2);
        rows["rb-sell"][2].ShouldBe(Guid.Parse(SellHandle));
        rows["rb-sell"][3].ShouldBe(DBNull.Value);

        foreach (var reference in SeedReferences)
        {
            rows[reference][1].ShouldBe(1, reference);
            rows[reference][2].ShouldBe(DBNull.Value, reference);
            rows[reference][3].ShouldBe(reference);
        }

        foreach (var memo in new[] { "hand", "reversal", "other" })
        {
            rows[memo][1].ShouldBe(0, memo);
            rows[memo][2].ShouldBe(DBNull.Value, memo);
        }

        rows["hand"][3].ShouldBe("hand-1");
        rows["other"][3].ShouldBe("TestSeed-Other");
        (await db.ReadAsync("SELECT [SourceKind] FROM [Transactions] WHERE [Memo] IS NULL"))
            .ShouldAllBe(row => (int)row[0] == 0);

        Flatten(await db.ReadAsync(LegsSql)).ShouldBe(cashBefore);
        Flatten(await db.ReadAsync(SecurityLegsSql)).ShouldBe(securityBefore);
        (await db.ReadAsync($"SELECT 1 FROM [SchemaVersions] WHERE [ScriptName] = N'{TransactionSourceScript}'"))
            .Count.ShouldBe(1);
    }

    [Test]
    public async Task Apply0035_WithARebalanceReferenceOnAnotherType_ThrowsAndChangesNothing()
    {
        await using var db = await ScratchSchemaDatabase.CreateAsync("TransactionSourcePrecheck");
        await db.ApplyBeforeAsync(TransactionSourceScript);
        await db.ExecuteAsync(ScratchSchemaDatabase.TenantRows(
                                  "a", Guid.NewGuid().ToString("N")[..12], transactionSource: false)
                              + BackfillRows()
                              + $"""
                                 INSERT INTO [Transactions] ([PublicId], [TenantId], [Type], [BookedAt], [Memo], [Reference], [Created], [CreatedBy])
                                 VALUES (NEWID(), @a_tenant, 0, SYSDATETIMEOFFSET(), N'rb-transfer', N'rb:{BuyHandle}:B:12', SYSDATETIMEOFFSET(), N'test');
                                 """);
        var before = Flatten(await db.ReadAsync("SELECT [Id], [Reference] FROM [Transactions] ORDER BY [Id]"));

        await Should.ThrowAsync<SqlException>(db.ApplyAllAsync);

        (await db.ReadAsync("""
                SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_NAME = N'Transactions' AND COLUMN_NAME IN (N'SourceKind', N'SourcePublicId')
                """))
            .ShouldBeEmpty();
        Flatten(await db.ReadAsync("SELECT [Id], [Reference] FROM [Transactions] ORDER BY [Id]")).ShouldBe(before);
        (await db.ReadAsync($"SELECT 1 FROM [SchemaVersions] WHERE [ScriptName] = N'{TransactionSourceScript}'"))
            .ShouldBeEmpty();
    }

    private const string LegsSql =
        "SELECT [TransactionId], [AccountId], [Amount], [Currency] FROM [TransactionCashLegs] ORDER BY [Id]";

    private const string SecurityLegsSql =
        "SELECT [TransactionId], [AccountId], [InstrumentId], [Quantity], [CostAmount] FROM [TransactionSecurityLegs] ORDER BY [Id]";

    /// <summary>
    /// Rows for the back-fill, on the schema before <c>0035</c>, after <see cref="ScratchSchemaDatabase.TenantRows"/>
    /// for prefix <c>a</c>. Each row's memo names it, because the script clears the rebalance references.
    /// </summary>
    private static string BackfillRows()
    {
        var seedRows = string.Concat(SeedReferences.Select(reference => $"""
            INSERT INTO [Transactions] ([PublicId], [TenantId], [Type], [BookedAt], [Memo], [Reference], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, 0, SYSDATETIMEOFFSET(), N'{reference}', N'{reference}', SYSDATETIMEOFFSET(), N'test');
            INSERT INTO [TransactionCashLegs] ([TenantId], [TransactionId], [AccountId], [Amount], [Currency], [Created], [CreatedBy])
            VALUES (@a_tenant, SCOPE_IDENTITY(), @a_brokerage, 5, 'NZD', SYSDATETIMEOFFSET(), N'test');

            """));
        return $"""
            INSERT INTO [Transactions] ([PublicId], [TenantId], [Type], [BookedAt], [Memo], [Reference], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, 7, SYSDATETIMEOFFSET(), N'rb-buy', N'rb:{BuyHandle}:B:' + CAST(@a_other AS nvarchar(10)), SYSDATETIMEOFFSET(), N'test');
            DECLARE @rb_buy int = SCOPE_IDENTITY();
            INSERT INTO [TransactionCashLegs] ([TenantId], [TransactionId], [AccountId], [Amount], [Currency], [Created], [CreatedBy])
            VALUES (@a_tenant, @rb_buy, @a_brokerage, -30, 'NZD', SYSDATETIMEOFFSET(), N'test');
            INSERT INTO [TransactionSecurityLegs] ([TenantId], [TransactionId], [AccountId], [InstrumentId], [Quantity], [CostAmount], [CostCurrency], [Created], [CreatedBy])
            VALUES (@a_tenant, @rb_buy, @a_brokerage, @a_other, 3, 15, 'USD', SYSDATETIMEOFFSET(), N'test');

            INSERT INTO [Transactions] ([PublicId], [TenantId], [Type], [BookedAt], [Memo], [Reference], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, 8, SYSDATETIMEOFFSET(), N'rb-sell', N'rb:{SellHandle}:S:' + CAST(@a_other AS nvarchar(10)), SYSDATETIMEOFFSET(), N'test');
            DECLARE @rb_sell int = SCOPE_IDENTITY();
            INSERT INTO [TransactionCashLegs] ([TenantId], [TransactionId], [AccountId], [Amount], [Currency], [Created], [CreatedBy])
            VALUES (@a_tenant, @rb_sell, @a_brokerage, 10, 'NZD', SYSDATETIMEOFFSET(), N'test');
            INSERT INTO [TransactionSecurityLegs] ([TenantId], [TransactionId], [AccountId], [InstrumentId], [Quantity], [CostAmount], [CostCurrency], [Created], [CreatedBy])
            VALUES (@a_tenant, @rb_sell, @a_brokerage, @a_other, -1, -5, 'USD', SYSDATETIMEOFFSET(), N'test');

            {seedRows}
            INSERT INTO [Transactions] ([PublicId], [TenantId], [Type], [BookedAt], [Memo], [Reference], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, 0, SYSDATETIMEOFFSET(), N'hand', N'hand-1', SYSDATETIMEOFFSET(), N'test');
            INSERT INTO [Transactions] ([PublicId], [TenantId], [Type], [BookedAt], [Memo], [Reference], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, 0, SYSDATETIMEOFFSET(), N'other', N'TestSeed-Other', SYSDATETIMEOFFSET(), N'test');
            INSERT INTO [Transactions] ([PublicId], [TenantId], [Type], [BookedAt], [Memo], [OriginalTransactionId], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, 5, SYSDATETIMEOFFSET(), N'reversal', @a_buy, SYSDATETIMEOFFSET(), N'test');

            """;
    }

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
            command.CommandText = ScratchSchemaDatabase.TenantRows(
                                      "a", Guid.NewGuid().ToString("N")[..12], transactionSource: true)
                                  + statement;
            return await command.ExecuteScalarAsync();
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }
}
