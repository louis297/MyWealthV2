using Microsoft.EntityFrameworkCore;
using MyWealthV2.Infrastructure.Data;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

public class RenameIsEnabledToIsActiveSchemaTests
{
    [Test]
    public async Task Apply_RenamesCatalogIsEnabledColumnsToIsActive()
    {
        await using var db = OpenDb();
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT TABLE_NAME, COLUMN_NAME
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_NAME IN (N'Currencies', N'Tenants', N'Instruments')
              AND COLUMN_NAME IN (N'IsEnabled', N'IsActive')
            ORDER BY TABLE_NAME, COLUMN_NAME
            """;

        var rows = new List<(string Table, string Column)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetString(0), reader.GetString(1)));
        }

        rows.ShouldBe([
            ("Currencies", "IsActive"),
            ("Instruments", "IsActive"),
            ("Tenants", "IsActive")
        ]);
    }

    [Test]
    public async Task Apply_RenamesCatalogDefaultConstraintsAndIndexes()
    {
        await using var db = OpenDb();
        await db.Database.OpenConnectionAsync();

        var defaults = await QueryNames(db, """
            SELECT dc.name
            FROM sys.default_constraints dc
            WHERE dc.name LIKE N'DF_%IsEnabled%' OR dc.name LIKE N'DF_%IsActive%'
            ORDER BY dc.name
            """);
        defaults.ShouldBe([
            "DF_Currencies_IsActive",
            "DF_Instruments_IsActive",
            "DF_Tenants_IsActive"
        ]);

        var indexes = await QueryNames(db, """
            SELECT i.name
            FROM sys.indexes i
            WHERE i.name LIKE N'%IsEnabled%' OR i.name LIKE N'%IsActive%'
            ORDER BY i.name
            """);
        indexes.ShouldBe([
            "IX_Instruments_TenantId_IsActive",
            "IX_Tenants_IsActive",
            "IX_Users_TenantId_IsActive"
        ]);
    }

    [Test]
    public async Task Apply_AddsUsersIsActivePersistedComputedFromStatus()
    {
        await using var db = OpenDb();
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT ty.name, c.is_nullable, cc.is_persisted, cc.definition
            FROM sys.columns c
            JOIN sys.types ty ON c.user_type_id = ty.user_type_id
            JOIN sys.computed_columns cc ON cc.object_id = c.object_id AND cc.column_id = c.column_id
            WHERE c.object_id = OBJECT_ID(N'Users') AND c.name = N'IsActive'
            """;

        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).ShouldBeTrue();
        reader.GetString(0).ShouldBe("bit");
        reader.GetBoolean(1).ShouldBeFalse();
        reader.GetBoolean(2).ShouldBeTrue();
        var definition = reader.GetString(3);
        definition.ShouldContain("[Status]", Case.Insensitive);
        definition.ShouldContain("(1)");
        (await reader.ReadAsync()).ShouldBeFalse();
    }

    [Test]
    public async Task Apply_AddsUsersTenantIdIsActiveIndex()
    {
        await using var db = OpenDb();
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT COL_NAME(ic.object_id, ic.column_id)
            FROM sys.indexes i
            JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
            WHERE i.object_id = OBJECT_ID(N'Users') AND i.name = N'IX_Users_TenantId_IsActive'
            ORDER BY ic.key_ordinal
            """;

        var columns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(0));
        }

        columns.ShouldBe(["TenantId", "IsActive"]);
    }

    private static async Task<List<string>> QueryNames(ApplicationDbContext db, string sql)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private static ApplicationDbContext OpenDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(SchemaDatabaseSetup.ConnectionString)
            .Options;
        return new ApplicationDbContext(options);
    }
}
