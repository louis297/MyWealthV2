using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Valuations;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Schema;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

/// <summary>Valuation run §6.1: the stored valuations, their lines, the runs and <c>Accounts.ValuedThrough</c>
/// (script 0059).</summary>
[Parallelizable]
public class StoredValuationsSchemaTests
{
    private const string Script = "0059_stored_valuations.sql";

    [Test]
    public void SchemaDirectory_Includes0059Script()
    {
        new FileSchemaScriptSource(ScratchSchemaDatabase.SchemaDirectory()).GetScripts()
            .Select(script => script.Name)
            .ShouldContain(Script);
    }

    [Test]
    public async Task ACustodialAccount_KeepsTheDayItWasValuedThrough()
    {
        (await RunAsync($"""
            {World()}
            UPDATE [Accounts] SET [ValuedThrough] = '2026-11-24' WHERE [Id] = @custodial;
            SELECT CONVERT(nvarchar(10), [ValuedThrough], 23) FROM [Accounts] WHERE [Id] = @custodial;
            """)).ShouldBe("2026-11-24");
    }

    [Test]
    public async Task AnExternalAccount_HasNoValuedThroughDate()
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            UPDATE [Accounts] SET [ValuedThrough] = '2026-11-24' WHERE [Id] = @a_bank;
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain("CK_Accounts_ValuedThrough");
    }

    [TestCase(0, false)]
    [TestCase(1, true)]
    public async Task ARunOfTheShape_Inserts(int trigger, bool ranBy)
    {
        (await RunAsync($"""
            {World()}
            {InsertRun(trigger, ranBy)}
            SELECT [Trigger] FROM [ValuationRuns] WHERE [Id] = SCOPE_IDENTITY();
            """)).ShouldBe(trigger);
    }

    [TestCase(0, true, "0, 0, 0, 0", "CK_ValuationRuns_RanBy")]
    [TestCase(1, false, "0, 0, 0, 0", "CK_ValuationRuns_RanBy")]
    [TestCase(2, true, "0, 0, 0, 0", "CK_ValuationRuns_Trigger")]
    [TestCase(0, false, "-1, 0, 0, 0", "CK_ValuationRuns_Counts")]
    [TestCase(0, false, "1, -1, 0, 0", "CK_ValuationRuns_Counts")]
    [TestCase(0, false, "1, 0, -1, 0", "CK_ValuationRuns_Counts")]
    [TestCase(0, false, "1, 0, 2, 3", "CK_ValuationRuns_Counts")]
    public async Task ARunOutsideTheChecks_Violates(int trigger, bool ranBy, string counts, string constraint)
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertRun(trigger, ranBy, counts)}
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain(constraint);
    }

    [Test]
    public async Task AValuationWithAValuedAndAStaleLine_Inserts()
    {
        (await RunAsync($"""
            {World()}
            {InsertRun(0, false)}
            DECLARE @run int = SCOPE_IDENTITY();
            {InsertValuation("2026-11-23", "1000", "500", "1500", "0", "1")}
            {InsertLine("@a_instrument", 0, price: true, fx: true, value: "500")}
            {InsertLine("@a_other", 2, price: true, fx: false, value: "NULL")}
            SELECT COUNT(*) FROM [StoredValuationLines] WHERE [StoredValuationId] = @valuation;
            """)).ShouldBe(2);
    }

    [Test]
    public async Task AnEmptyDay_Inserts()
    {
        (await RunAsync($"""
            {World()}
            {InsertRun(0, false)}
            DECLARE @run int = SCOPE_IDENTITY();
            {InsertValuation("2026-11-23", "0", "0", "0", "1", "0")}
            SELECT [Total] FROM [StoredValuations] WHERE [Id] = @valuation;
            """)).ShouldBe(0m);
    }

    [Test]
    public async Task ASecondValuationOfOneAccountAndDay_ViolatesTheKey()
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertRun(0, false)}
            DECLARE @run int = SCOPE_IDENTITY();
            {InsertValuation("2026-11-23", "0", "0", "0", "1", "0")}
            {InsertValuation("2026-11-23", "0", "0", "0", "1", "0", declare: false)}
            """));

        exception.Number.ShouldBe(2627);
        exception.Message.ShouldContain("UQ_StoredValuations_AccountId_ValuationDate");
    }

    [TestCase("100", "50", "160", "1", "0", "CK_StoredValuations_Total")]
    [TestCase("100", "0", "100", "1", "1", "CK_StoredValuations_Complete")]
    [TestCase("100", "0", "100", "0", "0", "CK_StoredValuations_Complete")]
    public async Task AValuationOutsideTheChecks_Violates(
        string cash, string holdings, string total, string complete, string unvalued, string constraint)
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertRun(0, false)}
            DECLARE @run int = SCOPE_IDENTITY();
            {InsertValuation("2026-11-23", cash, holdings, total, complete, unvalued)}
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain(constraint);
    }

    [Test]
    public async Task ASecondLineOfOneInstrument_ViolatesTheKey()
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertRun(0, false)}
            DECLARE @run int = SCOPE_IDENTITY();
            {InsertValuation("2026-11-23", "0", "0", "0", "0", "2")}
            {InsertLine("@a_instrument", 1, price: false, fx: false, value: "NULL")}
            {InsertLine("@a_instrument", 1, price: false, fx: false, value: "NULL")}
            """));

        exception.Number.ShouldBe(2627);
        exception.Message.ShouldContain("UQ_StoredValuationLines_StoredValuationId_InstrumentId");
    }

    [TestCase(7, true, true, "500", "CK_StoredValuationLines_Status")]
    [TestCase(0, true, true, "NULL", "CK_StoredValuationLines_Value")]
    [TestCase(2, true, false, "500", "CK_StoredValuationLines_Value")]
    public async Task ALineOutsideTheChecks_Violates(int status, bool price, bool fx, string value, string constraint)
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertRun(0, false)}
            DECLARE @run int = SCOPE_IDENTITY();
            {InsertValuation("2026-11-23", "0", "0", "0", "0", "1")}
            {InsertLine("@a_instrument", status, price, fx, value)}
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain(constraint);
    }

    [TestCase("[PriceAmount], [PriceCurrency], [PriceDate]", "41.3, 'USD', '2026-11-20'", "CK_StoredValuationLines_Price")]
    [TestCase("[FxRate], [FxDate]", "1.73, '2026-11-20'", "CK_StoredValuationLines_Fx")]
    public async Task APartPriceOrRate_Violates(string columns, string values, string constraint)
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertRun(0, false)}
            DECLARE @run int = SCOPE_IDENTITY();
            {InsertValuation("2026-11-23", "0", "0", "0", "0", "1")}
            INSERT INTO [StoredValuationLines]
                ([TenantId], [StoredValuationId], [InstrumentId], [Quantity], [LedgerQuantity], [Status], {columns})
            VALUES (@a_tenant, @valuation, @a_instrument, 1, 1, 1, {values});
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain(constraint);
    }

    [Test]
    public async Task EfRead_KeepsAStoredValuationWithItsLines_AndAccountValuedThrough()
    {
        await using var db = await ScratchSchemaDatabase.CreateAsync("StoredValuationsEf");
        await db.ApplyAllAsync();
        var row = (await db.ReadAsync($"""
            {World()}
            UPDATE [Accounts] SET [ValuedThrough] = '2026-11-23' WHERE [Id] = @custodial;
            {InsertRun(1, true)}
            DECLARE @run int = SCOPE_IDENTITY();
            {InsertValuation("2026-11-23", "1000", "500", "1500", "0", "1")}
            {InsertLine("@a_instrument", 0, price: true, fx: true, value: "500")}
            {InsertLine("@a_other", 2, price: true, fx: false, value: "NULL")}
            SELECT @valuation, @run, @custodial;
            """)).Single();
        var valuationId = Convert.ToInt64(row[0]);
        var runId = Convert.ToInt32(row[1]);
        var accountId = Convert.ToInt32(row[2]);

        await using var context = OpenDb(db.ConnectionString);
        var stored = await context.Set<StoredValuation>().AsNoTracking().Include(valuation => valuation.Lines)
            .SingleAsync(valuation => valuation.Id == valuationId);
        stored.AccountId.ShouldBe(accountId);
        stored.Date.ShouldBe(new DateOnly(2026, 11, 23));
        stored.Currency.ShouldBe("NZD");
        stored.Cash.ShouldBe(1000m);
        stored.HoldingsValue.ShouldBe(500m);
        stored.Total.ShouldBe(1500m);
        stored.Complete.ShouldBeFalse();
        stored.UnvaluedCount.ShouldBe(1);
        stored.ValuationRunId.ShouldBe(runId);
        var valued = stored.Lines.Single(line => line.Status == ValuationLineStatus.Valued);
        valued.ShouldSatisfyAllConditions(
            line => line.Quantity.ShouldBe(120m),
            line => line.LedgerQuantity.ShouldBe(120m),
            line => line.PriceAmount.ShouldBe(41.3m),
            line => line.PriceCurrency.ShouldBe("USD"),
            line => line.PriceDate.ShouldBe(new DateOnly(2026, 11, 20)),
            line => line.PriceSource.ShouldBe("EODHD"),
            line => line.MarketValue.ShouldBe(4956m),
            line => line.FxRate.ShouldBe(1.731m),
            line => line.FxDate.ShouldBe(new DateOnly(2026, 11, 20)),
            line => line.FxSource.ShouldBe("Frankfurter"),
            line => line.Value.ShouldBe(500m));
        var stale = stored.Lines.Single(line => line.Status == ValuationLineStatus.StalePrice);
        stale.FxRate.ShouldBeNull();
        stale.Value.ShouldBeNull();

        var run = await context.Set<ValuationRun>().AsNoTracking().SingleAsync(item => item.Id == runId);
        run.Trigger.ShouldBe(ValuationRunTrigger.Manual);
        run.ThroughDate.ShouldBe(new DateOnly(2026, 11, 24));
        run.RanByUserId.ShouldNotBeNull();

        (await context.Accounts.AsNoTracking().Where(item => item.Id == accountId)
                .Select(item => EF.Property<DateOnly?>(item, "ValuedThrough")).SingleAsync())
            .ShouldBe(new DateOnly(2026, 11, 23));
    }

    /// <summary>A tenant (<c>@a_*</c> from <see cref="ScratchSchemaDatabase.TenantRows"/>) whose brokerage account is
    /// made Custodial with a deposit reference, in <c>@custodial</c>.</summary>
    private static string World()
    {
        var code = Guid.NewGuid().ToString("N")[..12];
        return $"""
            {ScratchSchemaDatabase.TenantRows("a", code)}
            UPDATE [Accounts] SET [CustodyMode] = 1, [DepositReference] = UPPER(LEFT(N'{code}', 9)) WHERE [Id] = @a_brokerage;
            DECLARE @custodial int = @a_brokerage;
            """;
    }

    private static string InsertRun(int trigger, bool ranBy, string counts = "0, 0, 0, 0") => $"""
        INSERT INTO [ValuationRuns]
            ([PublicId], [TenantId], [ThroughDate], [Trigger], [RanByUserId], [StartedAt], [AccountsProcessed], [AccountsFailed],
             [DaysValued], [DaysIncomplete], [Created], [CreatedBy])
        VALUES (NEWID(), @a_tenant, '2026-11-24', {trigger}, {(ranBy ? "@a_adviser" : "NULL")}, SYSDATETIMEOFFSET(), {counts},
                SYSDATETIMEOFFSET(), N'test');
        """;

    /// <summary>A stored valuation <c>@valuation</c> of <c>@custodial</c> stored by <c>@run</c>.</summary>
    private static string InsertValuation(
        string day, string cash, string holdings, string total, string complete, string unvalued, bool declare = true) => $"""
        INSERT INTO [StoredValuations]
            ([TenantId], [AccountId], [ValuationDate], [Currency], [Cash], [HoldingsValue], [Total], [Complete], [UnvaluedCount],
             [ValuationRunId], [ValuedAt])
        VALUES (@a_tenant, @custodial, '{day}', 'NZD', {cash}, {holdings}, {total}, {complete}, {unvalued}, @run, SYSDATETIMEOFFSET());
        {(declare ? "DECLARE @valuation bigint = SCOPE_IDENTITY();" : "SET @valuation = SCOPE_IDENTITY();")}
        """;

    /// <summary>A line of <c>@valuation</c>: 120 at 41.30 USD with the rate 1.731 when <paramref name="fx"/>.</summary>
    private static string InsertLine(string instrument, int status, bool price, bool fx, string value) => $"""
        INSERT INTO [StoredValuationLines]
            ([TenantId], [StoredValuationId], [InstrumentId], [Quantity], [LedgerQuantity], [Status],
             [PriceAmount], [PriceCurrency], [PriceDate], [PriceSource], [MarketValue], [FxRate], [FxDate], [FxSource], [Value])
        VALUES (@a_tenant, @valuation, {instrument}, 120, 120, {status},
                {(price ? "41.3, 'USD', '2026-11-20', N'EODHD', 4956" : "NULL, NULL, NULL, NULL, NULL")},
                {(fx ? "1.731, '2026-11-20', N'Frankfurter'" : "NULL, NULL, NULL")}, {value});
        """;

    private static ApplicationDbContext OpenDb(string connectionString) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connectionString).Options);

    /// <summary>Runs <paramref name="statement"/> on the schema test database, then rolls back.</summary>
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
