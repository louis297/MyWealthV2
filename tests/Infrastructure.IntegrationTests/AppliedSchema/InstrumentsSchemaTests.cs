using Microsoft.EntityFrameworkCore;
using MyWealthV2.Infrastructure.Data;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

public class InstrumentsSchemaTests
{
    [Test]
    public async Task Apply_CreatesInstrumentsTableWithExpectedColumns()
    {
        await using var db = OpenDb();
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE, COLUMNPROPERTY(
                OBJECT_ID(TABLE_SCHEMA + '.' + TABLE_NAME), COLUMN_NAME, 'IsIdentity')
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_NAME = N'Instruments'
            ORDER BY ORDINAL_POSITION
            """;

        var columns = new List<(string Name, string Type, int? Length, string Nullable, int Identity)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add((
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetInt32(2),
                reader.GetString(3),
                reader.GetInt32(4)));
        }

        columns.Select(column => column.Name).ShouldBe([
            "Id", "PublicId", "TenantId", "Symbol", "Name", "QuoteCurrency", "IsActive",
            "Created", "CreatedBy", "LastModified", "LastModifiedBy", "RowVersion"
        ]);

        Column(columns, "Id").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("int"),
            column => column.Nullable.ShouldBe("NO"),
            column => column.Identity.ShouldBe(1));
        Column(columns, "PublicId").Type.ShouldBe("uniqueidentifier");
        Column(columns, "PublicId").Nullable.ShouldBe("NO");
        Column(columns, "TenantId").Type.ShouldBe("int");
        Column(columns, "TenantId").Nullable.ShouldBe("NO");
        Column(columns, "Symbol").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("nvarchar"),
            column => column.Length.ShouldBe(32),
            column => column.Nullable.ShouldBe("NO"));
        Column(columns, "Name").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("nvarchar"),
            column => column.Length.ShouldBe(200),
            column => column.Nullable.ShouldBe("NO"));
        Column(columns, "QuoteCurrency").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("char"),
            column => column.Length.ShouldBe(3),
            column => column.Nullable.ShouldBe("NO"));
        Column(columns, "IsActive").Type.ShouldBe("bit");
        Column(columns, "IsActive").Nullable.ShouldBe("NO");
        Column(columns, "Created").Type.ShouldBe("datetimeoffset");
        Column(columns, "Created").Nullable.ShouldBe("NO");
        Column(columns, "CreatedBy").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("nvarchar"),
            column => column.Length.ShouldBe(450),
            column => column.Nullable.ShouldBe("NO"));
        Column(columns, "LastModified").Type.ShouldBe("datetimeoffset");
        Column(columns, "LastModified").Nullable.ShouldBe("YES");
        Column(columns, "LastModifiedBy").Nullable.ShouldBe("YES");
        Column(columns, "RowVersion").Type.ShouldBe("timestamp");
        Column(columns, "RowVersion").Nullable.ShouldBe("NO");
    }

    [Test]
    public async Task Apply_DoesNotSeedInstrumentRows()
    {
        await using var db = OpenDb();
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM [Instruments]";
        var count = (int)(await command.ExecuteScalarAsync())!;
        count.ShouldBe(0);
    }

    [Test]
    public async Task Apply_AddsUniquePublicIdAndTenantSymbol()
    {
        await using var db = OpenDb();
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT kc.name, COL_NAME(ic.object_id, ic.column_id)
            FROM sys.key_constraints kc
            JOIN sys.index_columns ic ON ic.object_id = kc.parent_object_id AND ic.index_id = kc.unique_index_id
            WHERE kc.parent_object_id = OBJECT_ID(N'Instruments') AND kc.type = 'UQ'
            ORDER BY kc.name, ic.key_ordinal
            """;

        var rows = new List<(string Constraint, string Column)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetString(0), reader.GetString(1)));
        }

        rows.ShouldContain(("UQ_Instruments_PublicId", "PublicId"));
        rows.Where(row => row.Constraint == "UQ_Instruments_TenantId_Symbol")
            .Select(row => row.Column)
            .ShouldBe(["TenantId", "Symbol"]);
    }

    [Test]
    public async Task Apply_AddsForeignKeysRestrict()
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
            WHERE fk.parent_object_id = OBJECT_ID(N'Instruments')
            ORDER BY fk.name
            """;

        var rows = new List<(string Name, string Referenced, string Column, byte DeleteAction)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add((
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetByte(3)));
        }

        rows.ShouldBe([
            ("FK_Instruments_Currencies_QuoteCurrency", "Currencies", "Code", 0),
            ("FK_Instruments_Tenants_TenantId", "Tenants", "Id", 0)
        ]);
    }

    [Test]
    public async Task UniqueTenantSymbol_AllowsSameSymbolInAnotherTenant()
    {
        await using var db = OpenDb();
        var (tenantA, tenantB) = await TwoTenantFixture.InsertAsync(db);

        await db.Database.OpenConnectionAsync();
        await InsertInstrument(db, tenantA.Id, "VTI");
        await InsertInstrument(db, tenantB.Id, "VTI");

        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM [Instruments] WHERE [Symbol] = N'VTI'";
        var count = (int)(await command.ExecuteScalarAsync())!;
        count.ShouldBe(2);
    }

    private static async Task InsertInstrument(ApplicationDbContext db, int tenantId, string symbol)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            INSERT INTO [Instruments]
                ([PublicId], [TenantId], [Symbol], [Name], [QuoteCurrency], [IsActive], [Created], [CreatedBy])
            VALUES
                (NEWID(), @tenantId, @symbol, N'Test', 'NZD', 1, SYSDATETIMEOFFSET(), N'system')
            """;
        var tenant = command.CreateParameter();
        tenant.ParameterName = "@tenantId";
        tenant.Value = tenantId;
        command.Parameters.Add(tenant);
        var symbolParameter = command.CreateParameter();
        symbolParameter.ParameterName = "@symbol";
        symbolParameter.Value = symbol;
        command.Parameters.Add(symbolParameter);
        await command.ExecuteNonQueryAsync();
    }

    private static (string Name, string Type, int? Length, string Nullable, int Identity) Column(
        IReadOnlyList<(string Name, string Type, int? Length, string Nullable, int Identity)> columns,
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
