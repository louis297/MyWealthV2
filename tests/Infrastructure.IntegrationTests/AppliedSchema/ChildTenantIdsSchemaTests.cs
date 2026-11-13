using Microsoft.EntityFrameworkCore;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Data.Schema;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

/// <summary>Tenant composite keys B1: TenantId on the child tables (script 0027).</summary>
public class ChildTenantIdsSchemaTests
{
    private const string ChildTenantIdsScript = "0027_child_tenant_ids.sql";

    private static readonly string[] ChildTables =
    [
        "TransactionCashLegs",
        "TransactionSecurityLegs",
        "TransactionFxDetails",
        "PortfolioLines"
    ];

    private static readonly Type[] ChildTypes =
    [
        typeof(TransactionCashLeg),
        typeof(TransactionSecurityLeg),
        typeof(TransactionFxDetail),
        typeof(PortfolioLine)
    ];

    [Test]
    public void SchemaDirectory_Includes0027ChildTenantIdsScript()
    {
        new FileSchemaScriptSource(ScratchSchemaDatabase.SchemaDirectory()).GetScripts()
            .Select(script => script.Name)
            .ShouldContain(ChildTenantIdsScript);
    }

    [Test]
    public void Model_MapsARequiredTenantIdWithARestrictFkOnEveryChildType()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(SchemaDatabaseSetup.ConnectionString)
            .Options;
        using var db = new ApplicationDbContext(options);

        foreach (var type in ChildTypes)
        {
            var entity = db.Model.FindEntityType(type)!;
            var tenantId = entity.FindProperty("TenantId");
            tenantId.ShouldNotBeNull($"expected TenantId on {type.Name}");
            tenantId.IsNullable.ShouldBeFalse();
            entity.GetForeignKeys()
                .Single(key => key.PrincipalEntityType.ClrType == typeof(Tenant))
                .DeleteBehavior.ShouldBe(DeleteBehavior.Restrict);
        }
    }

    [Test]
    public async Task Apply_EveryChildTableHasANotNullIntTenantIdWithAFkToTenants()
    {
        var rows = await ScratchSchemaDatabase.ReadAsync(SchemaDatabaseSetup.ConnectionString, """
            SELECT t.[name], ty.[name], c.[is_nullable],
                   (SELECT COUNT(*)
                    FROM sys.foreign_key_columns fkc
                    JOIN sys.foreign_keys fk ON fk.[object_id] = fkc.[constraint_object_id]
                    WHERE fkc.[parent_object_id] = t.[object_id]
                      AND fkc.[parent_column_id] = c.[column_id]
                      AND fkc.[referenced_object_id] = OBJECT_ID(N'Tenants')
                      AND fk.[name] = N'FK_' + t.[name] + N'_Tenants_TenantId')
            FROM sys.tables t
            JOIN sys.columns c ON c.[object_id] = t.[object_id] AND c.[name] = N'TenantId'
            JOIN sys.types ty ON ty.[user_type_id] = c.[user_type_id]
            WHERE t.[name] IN (N'TransactionCashLegs', N'TransactionSecurityLegs', N'TransactionFxDetails', N'PortfolioLines')
            ORDER BY t.[name]
            """);

        rows.Select(row => ((string)row[0], (string)row[1], (bool)row[2], (int)row[3]))
            .ShouldBe(ChildTables.Order(StringComparer.Ordinal).Select(table => (table, "int", false, 1)));
    }

    [Test]
    public async Task Apply0027_BackFillsEveryChildRowFromItsParent()
    {
        new FileSchemaScriptSource(ScratchSchemaDatabase.SchemaDirectory()).GetScripts()
            .Select(script => script.Name)
            .ShouldContain(ChildTenantIdsScript);

        await using var db = await ScratchSchemaDatabase.CreateAsync("ChildTenantIdsBackfill");
        await db.ApplyBeforeAsync(ChildTenantIdsScript);
        await db.ExecuteAsync(
            ScratchSchemaDatabase.TenantRowsBefore0027("tenanta")
            + ScratchSchemaDatabase.TenantRowsBefore0027("tenantb"));

        var expected = Rows(await db.ReadAsync("""
            SELECT 'cash', l.[Id], h.[TenantId] FROM [TransactionCashLegs] l JOIN [Transactions] h ON h.[Id] = l.[TransactionId]
            UNION ALL
            SELECT 'security', l.[Id], h.[TenantId] FROM [TransactionSecurityLegs] l JOIN [Transactions] h ON h.[Id] = l.[TransactionId]
            UNION ALL
            SELECT 'fx', d.[TransactionId], h.[TenantId] FROM [TransactionFxDetails] d JOIN [Transactions] h ON h.[Id] = d.[TransactionId]
            UNION ALL
            SELECT 'line', l.[Id], p.[TenantId] FROM [PortfolioLines] l JOIN [Portfolios] p ON p.[Id] = l.[PortfolioId]
            ORDER BY 1, 2
            """));

        await db.ApplyAllAsync();

        var actual = Rows(await db.ReadAsync("""
            SELECT 'cash', [Id], [TenantId] FROM [TransactionCashLegs]
            UNION ALL
            SELECT 'security', [Id], [TenantId] FROM [TransactionSecurityLegs]
            UNION ALL
            SELECT 'fx', [TransactionId], [TenantId] FROM [TransactionFxDetails]
            UNION ALL
            SELECT 'line', [Id], [TenantId] FROM [PortfolioLines]
            ORDER BY 1, 2
            """));

        expected.Count.ShouldBe(12);
        expected.Select(row => row.TenantId).Distinct().Count().ShouldBe(2);
        actual.ShouldBe(expected);
    }

    private static List<(string Kind, int Id, int TenantId)> Rows(List<object[]> rows) =>
        rows.Select(row => ((string)row[0], (int)row[1], (int)row[2])).ToList();
}
