using Microsoft.EntityFrameworkCore;
using MyWealthV2.Infrastructure.Data;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

public class PortfoliosSchemaTests
{
    [Test]
    public async Task Apply_CreatesPortfolioTables()
    {
        await using var db = Open();
        await db.Database.OpenConnectionAsync();

        (await Columns(db, "Portfolios")).ShouldBe([
            "Id", "PublicId", "TenantId", "Name", "BaseCurrency", "IsActive",
            "DriftAbsolute", "DriftRelative", "MinimumTrade", "CashResidualPercent",
            "Created", "CreatedBy", "LastModified", "LastModifiedBy", "RowVersion"
        ]);
        (await Columns(db, "PortfolioLines")).ShouldBe([
            "Id", "PortfolioId", "InstrumentId", "TargetWeight", "SortOrder", "TenantId"
        ]);
        (await Columns(db, "AccountPortfolioAssignments")).ShouldBe([
            "Id", "PublicId", "TenantId", "AccountId", "PortfolioId", "Status",
            "AssignedAt", "AssignedBy", "LastRebalanceAt", "LastRebalanceStatus", "LastRebalanceMessage",
            "Created", "CreatedBy", "LastModified", "LastModifiedBy", "RowVersion"
        ]);

        (await ForeignKeyTargets(db, "Portfolios")).ShouldBe(["Currencies", "Tenants"], ignoreOrder: true);
        (await ForeignKeyTargets(db, "PortfolioLines")).ShouldBe(["Instruments", "Portfolios", "Tenants"], ignoreOrder: true);
        (await ForeignKeyTargets(db, "AccountPortfolioAssignments")).ShouldBe(
            ["Accounts", "Portfolios", "Tenants"], ignoreOrder: true);

        var filter = await Filter(db, "IX_AccountPortfolioAssignments_AccountId_Active");
        filter.ShouldContain("Status");
        filter.ShouldContain("0");
    }

    private static ApplicationDbContext Open()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(SchemaDatabaseSetup.ConnectionString)
            .Options;
        return new ApplicationDbContext(options);
    }

    private static async Task<List<string>> Columns(ApplicationDbContext db, string table)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT COLUMN_NAME
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_NAME = @table
            ORDER BY ORDINAL_POSITION
            """;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@table";
        parameter.Value = table;
        command.Parameters.Add(parameter);
        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private static async Task<List<string>> ForeignKeyTargets(ApplicationDbContext db, string table)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT OBJECT_NAME(fk.referenced_object_id)
            FROM sys.foreign_keys fk
            WHERE fk.parent_object_id = OBJECT_ID(@table)
            """;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@table";
        parameter.Value = table;
        command.Parameters.Add(parameter);
        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private static async Task<string> Filter(ApplicationDbContext db, string indexName)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT filter_definition
            FROM sys.indexes
            WHERE name = @name
            """;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@name";
        parameter.Value = indexName;
        command.Parameters.Add(parameter);
        var value = await command.ExecuteScalarAsync();
        value.ShouldNotBeNull();
        return (string)value;
    }
}
