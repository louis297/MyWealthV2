using MyWealthV2.Schema;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

/// <summary>Custody modes §6, R16: script 0032 removes every Active assignment on an External account.</summary>
[Parallelizable]
public class RemoveExternalPortfolioAssignmentsSchemaTests
{
    private const string Script = "0032_remove_external_portfolio_assignments.sql";

    private const string AssignmentsSql = """
        SELECT a.[Name], pa.[Status], pa.[LastModified], pa.[LastModifiedBy]
        FROM [AccountPortfolioAssignments] pa
        JOIN [Accounts] a ON a.[Id] = pa.[AccountId]
        ORDER BY a.[Name]
        """;

    [Test]
    public void SchemaDirectory_Includes0032Script()
    {
        new FileSchemaScriptSource(ScratchSchemaDatabase.SchemaDirectory()).GetScripts()
            .Select(script => script.Name)
            .ShouldContain(Script);
    }

    [Test]
    public async Task Apply0032_RemovesActiveAssignmentsOnExternalAccountsOnly()
    {
        await using var db = await ScratchSchemaDatabase.CreateAsync("RemoveExternalAssignments");
        await db.ApplyBeforeAsync(Script);
        await db.ExecuteAsync(ScratchSchemaDatabase.TenantRows("a", Guid.NewGuid().ToString("N")[..12], transactionSource: false) + """
            INSERT INTO [Accounts] ([PublicId], [TenantId], [CustomerId], [Name], [Type], [CustodyMode], [Status], [Currency], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @a_customer, N'Custodial', 2, 1, 0, 'NZD', SYSDATETIMEOFFSET(), N'test');
            DECLARE @a_custodial int = SCOPE_IDENTITY();
            INSERT INTO [Accounts] ([PublicId], [TenantId], [CustomerId], [Name], [Type], [CustodyMode], [Status], [Currency], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @a_customer, N'External removed', 2, 0, 0, 'NZD', SYSDATETIMEOFFSET(), N'test');
            DECLARE @a_removed int = SCOPE_IDENTITY();

            INSERT INTO [AccountPortfolioAssignments] ([PublicId], [TenantId], [AccountId], [PortfolioId], [Status], [AssignedAt], [AssignedBy], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @a_brokerage, @a_portfolio, 0, SYSDATETIMEOFFSET(), N'adviser', SYSDATETIMEOFFSET(), N'test'),
                   (NEWID(), @a_tenant, @a_custodial, @a_portfolio, 0, SYSDATETIMEOFFSET(), N'adviser', SYSDATETIMEOFFSET(), N'test');
            INSERT INTO [AccountPortfolioAssignments] ([PublicId], [TenantId], [AccountId], [PortfolioId], [Status], [AssignedAt], [AssignedBy], [Created], [CreatedBy], [LastModified], [LastModifiedBy])
            VALUES (NEWID(), @a_tenant, @a_removed, @a_portfolio, 1, SYSDATETIMEOFFSET(), N'adviser', SYSDATETIMEOFFSET(), N'test', '2026-01-01T00:00:00+00:00', N'adviser');

            INSERT INTO [Holdings] ([PublicId], [TenantId], [AccountId], [InstrumentId], [Quantity], [CostAmount], [CostCurrency], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @a_brokerage, @a_instrument, 1, 10, 'USD', SYSDATETIMEOFFSET(), N'test');
            """);
        var before = await CountsAsync(db);
        var startedAt = DateTimeOffset.UtcNow.AddSeconds(-5);

        await db.ApplyAllAsync();

        var rows = await db.ReadAsync(AssignmentsSql);
        rows.Select(row => (string)row[0]).ShouldBe(["Brokerage", "Custodial", "External removed"]);

        // Active on an External account → Removed, stamped by the system.
        ((int)rows[0][1]).ShouldBe(1);
        ((DateTimeOffset)rows[0][2]).ShouldBeGreaterThan(startedAt);
        rows[0][3].ShouldBe("system");

        // Active on a Custodial account → untouched.
        ((int)rows[1][1]).ShouldBe(0);
        rows[1][2].ShouldBe(DBNull.Value);
        rows[1][3].ShouldBe(DBNull.Value);

        // Already Removed → untouched.
        ((int)rows[2][1]).ShouldBe(1);
        ((DateTimeOffset)rows[2][2]).ShouldBe(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        rows[2][3].ShouldBe("adviser");

        (await CountsAsync(db)).ShouldBe(before);
        (await db.ReadAsync($"SELECT 1 FROM [SchemaVersions] WHERE [ScriptName] = N'{Script}'")).Count.ShouldBe(1);

        // A second run finds no row.
        await db.ExecuteAsync(await File.ReadAllTextAsync(
            Path.Combine(ScratchSchemaDatabase.SchemaDirectory(), Script)));
        (await db.ReadAsync(AssignmentsSql)).Select(Describe).ShouldBe(rows.Select(Describe));
    }

    private static async Task<(int Holdings, int Transactions)> CountsAsync(ScratchSchemaDatabase db)
    {
        var rows = await db.ReadAsync("SELECT (SELECT COUNT(*) FROM [Holdings]), (SELECT COUNT(*) FROM [Transactions])");
        return ((int)rows[0][0], (int)rows[0][1]);
    }

    private static string Describe(object[] row) => string.Join("|", row.Select(value => value.ToString()));
}
