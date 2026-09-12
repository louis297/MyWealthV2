using Microsoft.EntityFrameworkCore;
using MyWealthV2.Infrastructure.Data;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

public class TenantsReportingCurrencySchemaTests
{
    [Test]
    public async Task Apply_AddsReportingCurrencyNotNullWithoutDefault()
    {
        await using var db = OpenDb();
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_NAME = N'Tenants' AND COLUMN_NAME = N'ReportingCurrency'
            """;

        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).ShouldBeTrue();
        reader.GetString(0).ShouldBe("char");
        reader.GetInt32(1).ShouldBe(3);
        reader.GetString(2).ShouldBe("NO");
        (await reader.ReadAsync()).ShouldBeFalse();
    }

    [Test]
    public async Task Apply_DropsTemporaryReportingCurrencyDefault()
    {
        await using var db = OpenDb();
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM sys.default_constraints
            WHERE name = N'DF_Tenants_ReportingCurrency'
            """;

        var count = (int)(await command.ExecuteScalarAsync())!;
        count.ShouldBe(0);
    }

    [Test]
    public async Task Apply_AddsReportingCurrencyForeignKeyToCurrencies()
    {
        await using var db = OpenDb();
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT OBJECT_NAME(fk.referenced_object_id), COL_NAME(fkc.referenced_object_id, fkc.referenced_column_id), fk.delete_referential_action
            FROM sys.foreign_keys fk
            JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
            WHERE fk.name = N'FK_Tenants_Currencies_ReportingCurrency'
            """;

        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).ShouldBeTrue();
        reader.GetString(0).ShouldBe("Currencies");
        reader.GetString(1).ShouldBe("Code");
        reader.GetByte(2).ShouldBe((byte)0);
        (await reader.ReadAsync()).ShouldBeFalse();
    }

    private static ApplicationDbContext OpenDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(SchemaDatabaseSetup.ConnectionString)
            .Options;
        return new ApplicationDbContext(options);
    }
}

