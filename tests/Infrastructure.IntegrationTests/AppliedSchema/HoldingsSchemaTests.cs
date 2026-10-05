using Microsoft.EntityFrameworkCore;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Infrastructure.Data;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

public class HoldingsSchemaTests
{
    [Test]
    public async Task Apply_CreatesSecurityLegsAndHoldingsWithoutQuantityOrCostOnAccounts()
    {
        var securityColumns = await LoadColumns("TransactionSecurityLegs");
        securityColumns.Select(column => column.Name).ShouldBe([
            "Id", "TransactionId", "InstrumentId", "Quantity", "CostAmount", "CostCurrency",
            "Created", "CreatedBy", "LastModified", "LastModifiedBy", "RowVersion"
        ]);
        Column(securityColumns, "Id").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("int"),
            column => column.Nullable.ShouldBe("NO"),
            column => column.Identity.ShouldBe(1));
        Column(securityColumns, "TransactionId").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("int"),
            column => column.Nullable.ShouldBe("NO"));
        Column(securityColumns, "InstrumentId").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("int"),
            column => column.Nullable.ShouldBe("NO"));
        Column(securityColumns, "Quantity").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("decimal"),
            column => column.Precision.ShouldBe(18),
            column => column.Scale.ShouldBe(8),
            column => column.Nullable.ShouldBe("NO"));
        Column(securityColumns, "CostAmount").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("decimal"),
            column => column.Precision.ShouldBe(18),
            column => column.Scale.ShouldBe(4),
            column => column.Nullable.ShouldBe("NO"));
        Column(securityColumns, "CostCurrency").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("char"),
            column => column.Length.ShouldBe(3),
            column => column.Nullable.ShouldBe("NO"));
        Column(securityColumns, "Created").Nullable.ShouldBe("NO");
        Column(securityColumns, "CreatedBy").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("nvarchar"),
            column => column.Length.ShouldBe(450),
            column => column.Nullable.ShouldBe("NO"));
        Column(securityColumns, "LastModified").Nullable.ShouldBe("YES");
        Column(securityColumns, "LastModifiedBy").Nullable.ShouldBe("YES");
        Column(securityColumns, "RowVersion").Type.ShouldBe("timestamp");
        securityColumns.Select(column => column.Name).ShouldNotContain("PublicId");
        securityColumns.Select(column => column.Name).ShouldNotContain("Symbol");
        securityColumns.Select(column => column.Name).ShouldNotContain("Price");

        var holdingColumns = await LoadColumns("Holdings");
        holdingColumns.Select(column => column.Name).ShouldBe([
            "Id", "PublicId", "TenantId", "AccountId", "InstrumentId", "Quantity", "CostAmount", "CostCurrency",
            "Created", "CreatedBy", "LastModified", "LastModifiedBy", "RowVersion"
        ]);
        Column(holdingColumns, "Id").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("int"),
            column => column.Nullable.ShouldBe("NO"),
            column => column.Identity.ShouldBe(1));
        Column(holdingColumns, "PublicId").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("uniqueidentifier"),
            column => column.Nullable.ShouldBe("NO"));
        Column(holdingColumns, "TenantId").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("int"),
            column => column.Nullable.ShouldBe("NO"));
        Column(holdingColumns, "AccountId").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("int"),
            column => column.Nullable.ShouldBe("NO"));
        Column(holdingColumns, "InstrumentId").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("int"),
            column => column.Nullable.ShouldBe("NO"));
        Column(holdingColumns, "Quantity").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("decimal"),
            column => column.Precision.ShouldBe(18),
            column => column.Scale.ShouldBe(8),
            column => column.Nullable.ShouldBe("NO"));
        Column(holdingColumns, "CostAmount").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("decimal"),
            column => column.Precision.ShouldBe(18),
            column => column.Scale.ShouldBe(4),
            column => column.Nullable.ShouldBe("NO"));
        Column(holdingColumns, "CostCurrency").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("char"),
            column => column.Length.ShouldBe(3),
            column => column.Nullable.ShouldBe("NO"));
        Column(holdingColumns, "CreatedBy").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("nvarchar"),
            column => column.Length.ShouldBe(450),
            column => column.Nullable.ShouldBe("NO"));
        Column(holdingColumns, "LastModified").Nullable.ShouldBe("YES");
        Column(holdingColumns, "LastModifiedBy").Nullable.ShouldBe("YES");
        Column(holdingColumns, "RowVersion").Type.ShouldBe("timestamp");
        holdingColumns.Select(column => column.Name).ShouldNotContain("Symbol");
        holdingColumns.Select(column => column.Name).ShouldNotContain("Price");

        (await UniqueColumns("Holdings")).ShouldBe([
            ("UQ_Holdings_AccountId_InstrumentId", "AccountId"),
            ("UQ_Holdings_AccountId_InstrumentId", "InstrumentId"),
            ("UQ_Holdings_PublicId", "PublicId")
        ]);
        (await UniqueColumns("TransactionSecurityLegs")).ShouldBe([
            ("UQ_TransactionSecurityLegs_TransactionId_InstrumentId", "TransactionId"),
            ("UQ_TransactionSecurityLegs_TransactionId_InstrumentId", "InstrumentId")
        ]);

        (await ForeignKeys("TransactionSecurityLegs")).ShouldBe([
            ("FK_TransactionSecurityLegs_Currencies_CostCurrency", "Currencies", "Code", (byte)0),
            ("FK_TransactionSecurityLegs_Instruments_InstrumentId", "Instruments", "Id", (byte)0),
            ("FK_TransactionSecurityLegs_Transactions_TransactionId", "Transactions", "Id", (byte)0)
        ]);
        (await ForeignKeys("Holdings")).ShouldBe([
            ("FK_Holdings_Accounts_AccountId", "Accounts", "Id", (byte)0),
            ("FK_Holdings_Currencies_CostCurrency", "Currencies", "Code", (byte)0),
            ("FK_Holdings_Instruments_InstrumentId", "Instruments", "Id", (byte)0),
            ("FK_Holdings_Tenants_TenantId", "Tenants", "Id", (byte)0)
        ]);

        var accountColumns = await LoadColumns("Accounts");
        accountColumns.ShouldNotContain(column =>
            column.Name.Contains("Quantity", StringComparison.OrdinalIgnoreCase)
            || column.Name.Contains("Cost", StringComparison.OrdinalIgnoreCase));

        await using var db = OpenDb();
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM [TransactionSecurityLegs]),
                (SELECT COUNT(*) FROM [Holdings])
            """;
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).ShouldBeTrue();
        reader.GetInt32(0).ShouldBe(0);
        reader.GetInt32(1).ShouldBe(0);
        await reader.DisposeAsync();

        var security = db.Model.GetEntityTypes().Single(entity => entity.GetTableName() == "TransactionSecurityLegs");
        security.FindProperty("PublicId").ShouldBeNull();
        security.FindProperty("Symbol").ShouldBeNull();
        security.FindProperty("Price").ShouldBeNull();
        security.GetProperty("Quantity").GetColumnType().ShouldBe("decimal(18,8)");
        security.GetProperty("CostAmount").GetColumnType().ShouldBe("decimal(18,4)");
        security.GetProperty("CostCurrency").GetColumnType().ShouldBe("char(3)");
        UniqueIndexColumns(security).ShouldBe(["TransactionId,InstrumentId"]);
        security.GetForeignKeys().Single(key => key.Properties.Single().Name == "TransactionId")
            .DeleteBehavior.ShouldBe(DeleteBehavior.Restrict);

        var holding = db.Model.GetEntityTypes().Single(entity => entity.GetTableName() == "Holdings");
        holding.FindProperty("Symbol").ShouldBeNull();
        holding.FindProperty("Price").ShouldBeNull();
        holding.GetProperty("PublicId").IsNullable.ShouldBeFalse();
        holding.GetProperty("Quantity").GetColumnType().ShouldBe("decimal(18,8)");
        holding.GetProperty("CostAmount").GetColumnType().ShouldBe("decimal(18,4)");
        holding.GetProperty("CostCurrency").GetColumnType().ShouldBe("char(3)");
        UniqueIndexColumns(holding).ShouldBe(["AccountId,InstrumentId", "PublicId"]);

        var account = db.Model.FindEntityType(typeof(Account));
        account.ShouldNotBeNull();
        account.FindProperty("Quantity").ShouldBeNull();
        account.FindProperty("Cost").ShouldBeNull();
        account.FindProperty("CostAmount").ShouldBeNull();

        (await db.TransactionSecurityLegs.CountAsync()).ShouldBe(0);
        (await db.Holdings.CountAsync()).ShouldBe(0);
    }

    private static List<string> UniqueIndexColumns(Microsoft.EntityFrameworkCore.Metadata.IEntityType entity) =>
        entity.GetIndexes()
            .Where(index => index.IsUnique)
            .Select(index => string.Join(",", index.Properties.Select(property => property.Name)))
            .OrderBy(columns => columns, StringComparer.Ordinal)
            .ToList();

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
