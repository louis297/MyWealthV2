using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyWealthV2.Infrastructure.Data;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

public class InstrumentPricesSchemaTests
{
    [Test]
    public async Task Apply_CreatesEmptyInstrumentPricesWithTheLockedShape()
    {
        await using var db = Open();
        await db.Database.OpenConnectionAsync();

        var columns = await LoadColumns(db);
        columns.Select(column => column.Name).ShouldBe([
            "InstrumentId", "Price", "AsOfDate", "Source", "FetchedAt"
        ]);
        Column(columns, "InstrumentId").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("int"),
            column => column.Nullable.ShouldBe("NO"));
        Column(columns, "Price").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("decimal"),
            column => column.Precision.ShouldBe(18),
            column => column.Scale.ShouldBe(8),
            column => column.Nullable.ShouldBe("NO"));
        Column(columns, "AsOfDate").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("date"),
            column => column.Nullable.ShouldBe("NO"));
        Column(columns, "Source").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("nvarchar"),
            column => column.Length.ShouldBe(50),
            column => column.Nullable.ShouldBe("NO"));
        Column(columns, "FetchedAt").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("datetimeoffset"),
            column => column.Nullable.ShouldBe("NO"));

        (await PrimaryKeyColumns(db)).ShouldBe(["InstrumentId", "AsOfDate"]);
        (await IndexColumns(db, "IX_InstrumentPrices_FetchedAt")).ShouldBe(["FetchedAt"]);
        (await ForeignKeys(db)).ShouldBe([
            ("FK_InstrumentPrices_Instruments_InstrumentId", "Instruments", "InstrumentId", "Id", (byte)0)
        ]);

        (await Count(db)).ShouldBe(0);
    }

    [Test]
    public async Task Insert_UnknownInstrument_IsRejected()
    {
        await using var db = Open();
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            INSERT INTO [InstrumentPrices]
                ([InstrumentId], [Price], [AsOfDate], [Source], [FetchedAt])
            VALUES (2147483647, 250.00000000, '2026-01-01', N'TwelveData', SYSDATETIMEOFFSET())
            """;

        var exception = await Should.ThrowAsync<SqlException>(() => command.ExecuteNonQueryAsync());
        exception.Number.ShouldBe(547);
        (await Count(db)).ShouldBe(0);
    }

    private static ApplicationDbContext Open()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(SchemaDatabaseSetup.ConnectionString)
            .Options;
        return new ApplicationDbContext(options);
    }

    private static async Task<int> Count(ApplicationDbContext db)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM [InstrumentPrices]";
        return (int)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<List<(string Name, string Type, int? Length, int? Precision, int? Scale, string Nullable)>> LoadColumns(
        ApplicationDbContext db)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, NUMERIC_PRECISION, NUMERIC_SCALE, IS_NULLABLE
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_NAME = N'InstrumentPrices'
            ORDER BY ORDINAL_POSITION
            """;

        var columns = new List<(string Name, string Type, int? Length, int? Precision, int? Scale, string Nullable)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add((
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetInt32(2),
                reader.IsDBNull(3) ? null : Convert.ToInt32(reader.GetValue(3)),
                reader.IsDBNull(4) ? null : Convert.ToInt32(reader.GetValue(4)),
                reader.GetString(5)));
        }

        return columns;
    }

    private static async Task<List<string>> PrimaryKeyColumns(ApplicationDbContext db)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT COL_NAME(ic.object_id, ic.column_id)
            FROM sys.key_constraints kc
            JOIN sys.index_columns ic ON ic.object_id = kc.parent_object_id AND ic.index_id = kc.unique_index_id
            WHERE kc.parent_object_id = OBJECT_ID(N'InstrumentPrices') AND kc.type = 'PK'
            ORDER BY ic.key_ordinal
            """;

        var columns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }

    private static async Task<List<string>> IndexColumns(ApplicationDbContext db, string indexName)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT COL_NAME(ic.object_id, ic.column_id)
            FROM sys.indexes i
            JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
            WHERE i.object_id = OBJECT_ID(N'InstrumentPrices') AND i.name = @name
            ORDER BY ic.key_ordinal
            """;
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

    private static async Task<List<(string Name, string Referenced, string Column, string ReferencedColumn, byte DeleteAction)>> ForeignKeys(
        ApplicationDbContext db)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT fk.name, OBJECT_NAME(fk.referenced_object_id),
                   COL_NAME(fkc.parent_object_id, fkc.parent_column_id),
                   COL_NAME(fkc.referenced_object_id, fkc.referenced_column_id),
                   fk.delete_referential_action
            FROM sys.foreign_keys fk
            JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
            WHERE fk.parent_object_id = OBJECT_ID(N'InstrumentPrices')
            ORDER BY fk.name
            """;

        var rows = new List<(string Name, string Referenced, string Column, string ReferencedColumn, byte DeleteAction)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add((
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetByte(4)));
        }

        return rows;
    }

    private static (string Name, string Type, int? Length, int? Precision, int? Scale, string Nullable) Column(
        IReadOnlyList<(string Name, string Type, int? Length, int? Precision, int? Scale, string Nullable)> columns,
        string name) =>
        columns.Single(column => column.Name == name);
}
