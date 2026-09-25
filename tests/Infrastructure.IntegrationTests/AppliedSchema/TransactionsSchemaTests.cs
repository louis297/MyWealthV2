using Microsoft.EntityFrameworkCore;
using MyWealthV2.Infrastructure.Data;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

public class TransactionsSchemaTests
{
    [Test]
    public async Task Apply_CreatesTransactionsTableWithExpectedColumns()
    {
        var columns = await LoadColumns("Transactions");

        columns.Select(column => column.Name).ShouldBe([
            "Id", "PublicId", "TenantId", "AccountId", "Type", "BookedAt", "Memo", "Reference",
            "OriginalTransactionId", "Created", "CreatedBy", "LastModified", "LastModifiedBy", "RowVersion"
        ]);

        Column(columns, "Id").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("int"),
            column => column.Nullable.ShouldBe("NO"),
            column => column.Identity.ShouldBe(1));
        Column(columns, "PublicId").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("uniqueidentifier"),
            column => column.Nullable.ShouldBe("NO"));
        Column(columns, "TenantId").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("int"),
            column => column.Nullable.ShouldBe("NO"));
        Column(columns, "AccountId").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("int"),
            column => column.Nullable.ShouldBe("NO"));
        Column(columns, "Type").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("int"),
            column => column.Nullable.ShouldBe("NO"));
        Column(columns, "BookedAt").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("datetimeoffset"),
            column => column.Nullable.ShouldBe("NO"));
        Column(columns, "Memo").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("nvarchar"),
            column => column.Length.ShouldBe(200),
            column => column.Nullable.ShouldBe("YES"));
        Column(columns, "Reference").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("nvarchar"),
            column => column.Length.ShouldBe(100),
            column => column.Nullable.ShouldBe("YES"));
        Column(columns, "OriginalTransactionId").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("int"),
            column => column.Nullable.ShouldBe("YES"));
        Column(columns, "Created").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("datetimeoffset"),
            column => column.Nullable.ShouldBe("NO"));
        Column(columns, "CreatedBy").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("nvarchar"),
            column => column.Length.ShouldBe(450),
            column => column.Nullable.ShouldBe("NO"));
        Column(columns, "LastModified").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("datetimeoffset"),
            column => column.Nullable.ShouldBe("YES"));
        Column(columns, "LastModifiedBy").Nullable.ShouldBe("YES");
        Column(columns, "RowVersion").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("timestamp"),
            column => column.Nullable.ShouldBe("NO"));
    }

    [Test]
    public async Task Apply_CreatesTransactionCashLegsTableWithExpectedColumns()
    {
        var columns = await LoadColumns("TransactionCashLegs");

        columns.Select(column => column.Name).ShouldBe([
            "Id", "TransactionId", "Amount", "Currency",
            "Created", "CreatedBy", "LastModified", "LastModifiedBy", "RowVersion"
        ]);

        Column(columns, "Id").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("int"),
            column => column.Nullable.ShouldBe("NO"),
            column => column.Identity.ShouldBe(1));
        Column(columns, "TransactionId").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("int"),
            column => column.Nullable.ShouldBe("NO"));
        Column(columns, "Amount").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("decimal"),
            column => column.Precision.ShouldBe(18),
            column => column.Scale.ShouldBe(4),
            column => column.Nullable.ShouldBe("NO"));
        Column(columns, "Currency").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("char"),
            column => column.Length.ShouldBe(3),
            column => column.Nullable.ShouldBe("NO"));
        Column(columns, "Created").Nullable.ShouldBe("NO");
        Column(columns, "CreatedBy").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("nvarchar"),
            column => column.Length.ShouldBe(450),
            column => column.Nullable.ShouldBe("NO"));
        Column(columns, "LastModified").Nullable.ShouldBe("YES");
        Column(columns, "LastModifiedBy").Nullable.ShouldBe("YES");
        Column(columns, "RowVersion").Type.ShouldBe("timestamp");
        columns.Select(column => column.Name).ShouldNotContain("PublicId");
    }

    [Test]
    public async Task Apply_CreatesIdempotencyRecordsTableWithExpectedColumns()
    {
        var columns = await LoadColumns("IdempotencyRecords");

        columns.Select(column => column.Name).ShouldBe([
            "Id", "TenantId", "Key", "Method", "Path", "RequestHash", "ResponseStatus", "ResponseBody", "Created"
        ]);

        Column(columns, "Id").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("int"),
            column => column.Nullable.ShouldBe("NO"),
            column => column.Identity.ShouldBe(1));
        Column(columns, "TenantId").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("int"),
            column => column.Nullable.ShouldBe("NO"));
        Column(columns, "Key").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("uniqueidentifier"),
            column => column.Nullable.ShouldBe("NO"));
        Column(columns, "Method").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("nvarchar"),
            column => column.Length.ShouldBe(10),
            column => column.Nullable.ShouldBe("NO"));
        Column(columns, "Path").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("nvarchar"),
            column => column.Length.ShouldBe(200),
            column => column.Nullable.ShouldBe("NO"));
        Column(columns, "RequestHash").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("nvarchar"),
            column => column.Length.ShouldBe(64),
            column => column.Nullable.ShouldBe("NO"));
        Column(columns, "ResponseStatus").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("int"),
            column => column.Nullable.ShouldBe("NO"));
        Column(columns, "ResponseBody").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("nvarchar"),
            column => column.Length.ShouldBe(-1),
            column => column.Nullable.ShouldBe("NO"));
        Column(columns, "Created").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("datetimeoffset"),
            column => column.Nullable.ShouldBe("NO"));
    }

    [Test]
    public async Task Apply_DoesNotAddBalanceColumnOrJournalsTableOrSeedRows()
    {
        var accountColumns = await LoadColumns("Accounts");
        accountColumns.Select(column => column.Name).ShouldNotContain("Balance");

        await using var db = OpenDb();
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = N'Journals'),
                (SELECT COUNT(*) FROM [Transactions]),
                (SELECT COUNT(*) FROM [TransactionCashLegs]),
                (SELECT COUNT(*) FROM [IdempotencyRecords])
            """;

        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).ShouldBeTrue();
        reader.GetInt32(0).ShouldBe(0);
        reader.GetInt32(1).ShouldBe(0);
        reader.GetInt32(2).ShouldBe(0);
        reader.GetInt32(3).ShouldBe(0);
    }

    [Test]
    public async Task Apply_AddsUniqueKeysAndBookedAtIndex()
    {
        (await UniqueColumns("Transactions")).ShouldContain(("UQ_Transactions_PublicId", "PublicId"));
        (await UniqueColumns("TransactionCashLegs")).ShouldContain(("UQ_TransactionCashLegs_TransactionId", "TransactionId"));
        (await UniqueColumns("IdempotencyRecords")).ShouldBe([
            ("UQ_IdempotencyRecords_TenantId_Key", "TenantId"),
            ("UQ_IdempotencyRecords_TenantId_Key", "Key")
        ]);

        (await IndexColumns("Transactions", "IX_Transactions_TenantId_AccountId_BookedAt"))
            .ShouldBe(["TenantId", "AccountId", "BookedAt"]);
    }

    [Test]
    public async Task Apply_AddsForeignKeysRestrict()
    {
        (await ForeignKeys("Transactions")).ShouldBe([
            ("FK_Transactions_Accounts_AccountId", "Accounts", "Id", (byte)0),
            ("FK_Transactions_Tenants_TenantId", "Tenants", "Id", (byte)0),
            ("FK_Transactions_Transactions_OriginalTransactionId", "Transactions", "Id", (byte)0)
        ]);
        (await ForeignKeys("TransactionCashLegs")).ShouldBe([
            ("FK_TransactionCashLegs_Currencies_Currency", "Currencies", "Code", (byte)0),
            ("FK_TransactionCashLegs_Transactions_TransactionId", "Transactions", "Id", (byte)0)
        ]);
        (await ForeignKeys("IdempotencyRecords")).ShouldBe([
            ("FK_IdempotencyRecords_Tenants_TenantId", "Tenants", "Id", (byte)0)
        ]);
    }

    [Test]
    public async Task Apply_AddsTypeCheck()
    {
        await using var db = OpenDb();
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT cc.name, cc.definition
            FROM sys.check_constraints cc
            WHERE cc.parent_object_id = OBJECT_ID(N'Transactions')
            ORDER BY cc.name
            """;

        var rows = new List<(string Name, string Definition)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetString(0), reader.GetString(1)));
        }

        var type = rows.Single(row => row.Name == "CK_Transactions_Type");
        type.Definition.ShouldContain("[Type]", Case.Insensitive);
        type.Definition.ShouldContain("0");
        type.Definition.ShouldContain("8");
    }

    private static async Task<List<(string Name, string Type, int? Length, int? Precision, int? Scale, string Nullable, int Identity)>> LoadColumns(
        string tableName)
    {
        await using var db = OpenDb();
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, NUMERIC_PRECISION, NUMERIC_SCALE, IS_NULLABLE,
                   COLUMNPROPERTY(OBJECT_ID(TABLE_SCHEMA + '.' + TABLE_NAME), COLUMN_NAME, 'IsIdentity')
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_NAME = @tableName
            ORDER BY ORDINAL_POSITION
            """;
        var table = command.CreateParameter();
        table.ParameterName = "@tableName";
        table.Value = tableName;
        command.Parameters.Add(table);

        var columns = new List<(string Name, string Type, int? Length, int? Precision, int? Scale, string Nullable, int Identity)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add((
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetInt32(2),
                reader.IsDBNull(3) ? null : Convert.ToInt32(reader.GetValue(3)),
                reader.IsDBNull(4) ? null : Convert.ToInt32(reader.GetValue(4)),
                reader.GetString(5),
                reader.IsDBNull(6) ? 0 : reader.GetInt32(6)));
        }

        return columns;
    }

    private static async Task<List<(string Constraint, string Column)>> UniqueColumns(string tableName)
    {
        await using var db = OpenDb();
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT kc.name, COL_NAME(ic.object_id, ic.column_id)
            FROM sys.key_constraints kc
            JOIN sys.index_columns ic ON ic.object_id = kc.parent_object_id AND ic.index_id = kc.unique_index_id
            WHERE kc.parent_object_id = OBJECT_ID(@tableName) AND kc.type = 'UQ'
            ORDER BY kc.name, ic.key_ordinal
            """;
        var table = command.CreateParameter();
        table.ParameterName = "@tableName";
        table.Value = tableName;
        command.Parameters.Add(table);

        var rows = new List<(string Constraint, string Column)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetString(0), reader.GetString(1)));
        }

        return rows;
    }

    private static async Task<List<string>> IndexColumns(string tableName, string indexName)
    {
        await using var db = OpenDb();
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT COL_NAME(ic.object_id, ic.column_id)
            FROM sys.indexes i
            JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
            WHERE i.object_id = OBJECT_ID(@tableName) AND i.name = @name
            ORDER BY ic.key_ordinal
            """;
        var table = command.CreateParameter();
        table.ParameterName = "@tableName";
        table.Value = tableName;
        command.Parameters.Add(table);
        var name = command.CreateParameter();
        name.ParameterName = "@name";
        name.Value = indexName;
        command.Parameters.Add(name);

        var columns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }

    private static async Task<List<(string Name, string Referenced, string Column, byte DeleteAction)>> ForeignKeys(
        string tableName)
    {
        await using var db = OpenDb();
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT fk.name, OBJECT_NAME(fk.referenced_object_id),
                   COL_NAME(fkc.referenced_object_id, fkc.referenced_column_id),
                   fk.delete_referential_action
            FROM sys.foreign_keys fk
            JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
            WHERE fk.parent_object_id = OBJECT_ID(@tableName)
            ORDER BY fk.name
            """;
        var table = command.CreateParameter();
        table.ParameterName = "@tableName";
        table.Value = tableName;
        command.Parameters.Add(table);

        var rows = new List<(string Name, string Referenced, string Column, byte DeleteAction)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetByte(3)));
        }

        return rows;
    }

    private static (string Name, string Type, int? Length, int? Precision, int? Scale, string Nullable, int Identity) Column(
        IReadOnlyList<(string Name, string Type, int? Length, int? Precision, int? Scale, string Nullable, int Identity)> columns,
        string name) =>
        columns.Single(column => column.Name == name);

    private static ApplicationDbContext OpenDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(SchemaDatabaseSetup.ConnectionString)
            .Options;
        return new ApplicationDbContext(options);
    }
}
