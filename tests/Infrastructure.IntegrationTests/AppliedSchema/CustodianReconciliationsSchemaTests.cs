using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.ValueObjects;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Schema;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

/// <summary>In-kind transfers §6.2: custodian reconciliation runs and their lines (script 0056).</summary>
public class CustodianReconciliationsSchemaTests
{
    [Test]
    public void SchemaDirectory_Includes0056Script() =>
        new FileSchemaScriptSource(ScratchSchemaDatabase.SchemaDirectory()).GetScripts()
            .Select(script => script.Name)
            .ShouldContain("0056_custodian_reconciliations.sql");

    [TestCase("0", "NULL", "0", "SYSDATETIMEOFFSET()")]
    [TestCase("1", "@a_adviser", "1", "SYSDATETIMEOFFSET()")]
    [TestCase("0", "NULL", "2", "NULL")]
    public async Task ARun_WithItsColumns_Inserts(string trigger, string ranBy, string status, string positionsAt)
    {
        (await RunAsync($"""
            {World()}
            {Run(trigger, ranBy, status, positionsAt)}
            SELECT [Status] FROM [CustodianReconciliations] WHERE [Id] = @run;
            """)).ShouldBe(int.Parse(status));
    }

    [TestCase("0", "@a_adviser", "0", "SYSDATETIMEOFFSET()", "CK_CustodianReconciliations_RanBy")]
    [TestCase("1", "NULL", "0", "SYSDATETIMEOFFSET()", "CK_CustodianReconciliations_RanBy")]
    [TestCase("0", "NULL", "2", "SYSDATETIMEOFFSET()", "CK_CustodianReconciliations_PositionsAt")]
    [TestCase("0", "NULL", "1", "NULL", "CK_CustodianReconciliations_PositionsAt")]
    [TestCase("0", "NULL", "3", "SYSDATETIMEOFFSET()", "CK_CustodianReconciliations_Status")]
    public async Task ARunOutsideTheChecks_Violates(string trigger, string ranBy, string status, string positionsAt, string constraint)
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {Run(trigger, ranBy, status, positionsAt)}
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain(constraint);
    }

    [TestCase("100", "0", "100", "0", "0", "0", "0", "100", "")]
    [TestCase("0", "1200", "-1200", "0", "-200", "0", "0", "-900", "CK_CustodianReconciliationLines_Components")]
    [TestCase("10", "5", "4", "0", "0", "0", "0", "4", "CK_CustodianReconciliationLines_Difference")]
    public async Task ALine_MustAddUp(
        string custodian, string platform, string difference, string notProcessed, string unsettled, string suspense, string unbooked,
        string unexplained, string constraint)
    {
        var statement = $"""
            {World()}
            {Run("0", "NULL", "1", "SYSDATETIMEOFFSET()")}
            INSERT INTO [CustodianReconciliationLines]
                ([TenantId], [ReconciliationId], [Position], [Symbol], [InstrumentId], [CustodianQuantity], [PlatformQuantity],
                 [Difference], [NotProcessed], [UnsettledTrades], [Suspense], [UnbookedFills], [Unexplained], [Class])
            VALUES (@a_tenant, @run, 1, N'VTI', @a_instrument, {custodian}, {platform}, {difference}, {notProcessed}, {unsettled},
                    {suspense}, {unbooked}, {unexplained}, 0);
            SELECT COUNT(*) FROM [CustodianReconciliationLines] WHERE [ReconciliationId] = @run;
            """;
        if (constraint.Length == 0)
        {
            (await RunAsync(statement)).ShouldBe(1);
            return;
        }

        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync(statement));
        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain(constraint);
    }

    [Test]
    public async Task EfRoundTrip_KeepsARunAndItsLines()
    {
        await using var db = await ScratchSchemaDatabase.CreateAsync("CustodianReconciliationsEf");
        await db.ApplyAllAsync();
        var row = (await db.ReadAsync($"""
            {ScratchSchemaDatabase.TenantRows("a", Guid.NewGuid().ToString("N")[..12])}
            SELECT @a_tenant, @a_instrument, @a_adviser;
            """)).Single();
        var ids = row.Select(Convert.ToInt32).ToArray();
        var at = new DateTimeOffset(2026, 11, 24, 2, 0, 0, TimeSpan.Zero);
        int runId;
        await using (var context = OpenDb(db.ConnectionString))
        {
            var usd = await context.Set<Currency>().SingleAsync(c => c.Code == "USD");
            var safekeeping = SafekeepingAccount.Create(ids[0], usd, "Custody", "Nominees", "SK-RUN-1", "simulator");
            safekeeping.CreatedBy = "test";
            context.Set<SafekeepingAccount>().Add(safekeeping);
            await context.SaveChangesAsync();
            var run = CustodianReconciliation.Compute(new CustodianReconciliationInputs
            {
                TenantId = ids[0],
                SafekeepingAccountId = safekeeping.Id,
                Currency = "USD",
                PositionsAt = at,
                Positions = [new CustodianSymbolQuantity("VTI", 50m), new CustodianSymbolQuantity("OTHER", 1m)],
                Platform = [new CustodianInstrumentQuantity(ids[1], "VTI", 60m)],
                NotProcessed = [],
                Suspense = [],
                FillsNotDelivered = [new CustodianInstrumentQuantity(ids[1], "VTI", 10m)],
                DeliveredPendingFills = [],
                TransfersInFlight = 1,
                FillsNotDeliveredCount = 1
            }, ReconciliationTrigger.Manual, ids[2], at);
            run.CreatedBy = "test";
            context.Set<CustodianReconciliation>().Add(run);
            await context.SaveChangesAsync();
            runId = run.Id;
        }

        await using (var context = OpenDb(db.ConnectionString))
        {
            var stored = await context.Set<CustodianReconciliation>().AsNoTracking().Include(run => run.Lines)
                .SingleAsync(run => run.Id == runId);
            (stored.Status, stored.RanByUserId, stored.TransfersInFlight).ShouldBe((CustodianReconciliationStatus.Breaks, (int?)ids[2], 1));
            stored.Lines.OrderBy(line => line.Position).Select(line => (line.Symbol, line.UnsettledTrades, line.Unexplained, line.Class))
                .ShouldBe([("OTHER", 0m, 1m, CustodianLineClass.Break), ("VTI", -10m, 0m, CustodianLineClass.Timing)]);
        }
    }

    private static string World() => ScratchSchemaDatabase.TenantRows("a", Guid.NewGuid().ToString("N")[..12]) + $"""

        INSERT INTO [SafekeepingAccounts]
            ([PublicId], [TenantId], [Currency], [CustodianName], [AccountName], [AccountNumber], [CustodianConnection], [IsActive], [Created], [CreatedBy])
        VALUES (NEWID(), @a_tenant, 'USD', N'Custody', N'Nominees', N'SK-{Guid.NewGuid().ToString("N")[..12]}', N'simulator', 0,
                SYSDATETIMEOFFSET(), N'test');
        DECLARE @sk int = SCOPE_IDENTITY();
        """;

    private static string Run(string trigger, string ranBy, string status, string positionsAt) => $"""
        INSERT INTO [CustodianReconciliations]
            ([PublicId], [TenantId], [SafekeepingAccountId], [Currency], [Trigger], [RanAt], [RanByUserId], [Status], [PositionsAt],
             [BreakCount], [TimingCount], [AgreedCount], [TransfersInFlight], [FillsNotDelivered], [Created], [CreatedBy])
        VALUES (NEWID(), @a_tenant, @sk, 'USD', {trigger}, SYSDATETIMEOFFSET(), {ranBy}, {status}, {positionsAt}, 0, 0, 0, 0, 0,
                SYSDATETIMEOFFSET(), N'test');
        DECLARE @run int = SCOPE_IDENTITY();
        """;

    private static ApplicationDbContext OpenDb(string connectionString) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connectionString).Options);

    private static async Task<object?> RunAsync(string statement)
    {
        await using var connection = new SqlConnection(SchemaDatabaseSetup.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = statement;
            return await command.ExecuteScalarAsync();
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }
}
