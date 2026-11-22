using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.ValueObjects;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Schema;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

/// <summary>Trade fees §6: cash leg roles, trade fee schedules and bands, and fee details (script 0030).</summary>
public class TradeFeesSchemaTests
{
    private const string TradeFeesScript = "0030_trade_fees.sql";

    private const string FeeDetail = """
        INSERT INTO [TransactionFeeDetails] ([CashLegId], [TenantId], [TransactionId], [Kind], [TradeFeeScheduleId], [BasisAmount], [FeeAmount], [Currency])
        VALUES (@a_feeleg, @a_tenant, @a_buy, {0}, {1}, {2}, {3}, 'USD');
        """;

    [Test]
    public void SchemaDirectory_Includes0030TradeFeesScript()
    {
        new FileSchemaScriptSource(ScratchSchemaDatabase.SchemaDirectory()).GetScripts()
            .Select(script => script.Name)
            .ShouldContain(TradeFeesScript);
    }

    [Test]
    public async Task Apply_CreatesTradeFeeColumnsAndTables()
    {
        (await Columns("TransactionCashLegs")).ShouldContain("Role");
        (await Columns("TradeFeeSchedules")).ShouldBe([
            "Id", "PublicId", "TenantId", "QuoteCurrency", "EffectiveFrom", "MinimumAmount", "CapAmount",
            "Created", "CreatedBy", "LastModified", "LastModifiedBy", "RowVersion"
        ]);
        (await Columns("TradeFeeBands")).ShouldBe(["Id", "TenantId", "TradeFeeScheduleId", "FromAmount", "RatePercent"]);
        (await Columns("TransactionFeeDetails")).ShouldBe([
            "CashLegId", "TenantId", "TransactionId", "Kind", "TradeFeeScheduleId", "BasisAmount", "FeeAmount", "Currency"
        ]);
    }

    [Test]
    public async Task Apply0030_ExistingCashLegsReadAsPrincipal()
    {
        await using var db = await ScratchSchemaDatabase.CreateAsync("TradeFeesRoles");
        await db.ApplyBeforeAsync(TradeFeesScript);
        await db.ExecuteAsync(ScratchSchemaDatabase.TenantRows("a", Guid.NewGuid().ToString("N")[..12], custodyMode: false, transactionSource: false));

        await db.ApplyAllAsync();

        var roles = await db.ReadAsync("SELECT [Role] FROM [TransactionCashLegs]");
        roles.Count.ShouldBe(3);
        roles.ShouldAllBe(row => (int)row[0] == 0);
    }

    [Test]
    public async Task Insert_CashLegWithoutRole_IsPrincipal()
    {
        var role = await RunAsync("""
            SELECT [Role] FROM [TransactionCashLegs] WHERE [TransactionId] = @a_buy AND [Id] <> @a_feeleg;
            """);

        role.ShouldBe(0);
    }

    [Test]
    public async Task Insert_SecondPrincipalLegOnSameAccount_IsRejected()
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync("""
            INSERT INTO [TransactionCashLegs] ([TenantId], [TransactionId], [AccountId], [Role], [Amount], [Currency], [Created], [CreatedBy])
            VALUES (@a_tenant, @a_buy, @a_brokerage, 0, -5, 'NZD', SYSDATETIMEOFFSET(), N'test');
            """));

        exception.Number.ShouldBe(2601);
    }

    [Test]
    public async Task Insert_PrincipalAndFeeLegOnSameAccount_AreAccepted()
    {
        var count = await RunAsync("""
            SELECT COUNT(*) FROM [TransactionCashLegs] WHERE [TransactionId] = @a_buy AND [AccountId] = @a_brokerage;
            """);

        count.ShouldBe(2);
    }

    [TestCase(-1)]
    [TestCase(3)]
    public async Task Insert_UnknownRole_IsRejected(int role)
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            INSERT INTO [TransactionCashLegs] ([TenantId], [TransactionId], [AccountId], [Role], [Amount], [Currency], [Created], [CreatedBy])
            VALUES (@a_tenant, @a_buy, @a_brokerage, {role}, -5, 'NZD', SYSDATETIMEOFFSET(), N'test');
            """));

        exception.Number.ShouldBe(547);
    }

    [Test]
    public async Task Insert_FeeDetail_IsAccepted()
    {
        var count = await RunAsync(Detail(0, "@a_feeschedule", "1250", "10")
            + "SELECT COUNT(*) FROM [TransactionFeeDetails] WHERE [CashLegId] = @a_feeleg;");

        count.ShouldBe(1);
    }

    [Test]
    public async Task Insert_SecondFeeOfSameKindOnOneTransaction_IsRejected()
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync(Detail(0, "@a_feeschedule", "1250", "10") + """
            INSERT INTO [TransactionCashLegs] ([TenantId], [TransactionId], [AccountId], [Role], [Amount], [Currency], [Created], [CreatedBy])
            VALUES (@a_tenant, @a_buy, @a_brokerage, 1, -3, 'NZD', SYSDATETIMEOFFSET(), N'test');
            DECLARE @second_feeleg int = SCOPE_IDENTITY();
            INSERT INTO [TransactionFeeDetails] ([CashLegId], [TenantId], [TransactionId], [Kind], [TradeFeeScheduleId], [BasisAmount], [FeeAmount], [Currency])
            VALUES (@second_feeleg, @a_tenant, @a_buy, 0, @a_feeschedule, 1250, 3, 'USD');
            """));

        exception.Number.ShouldBe(2627);
    }

    private static IEnumerable<TestCaseData> InvalidDetails()
    {
        yield return new TestCaseData(1, "@a_feeschedule", "1250", "10").SetName("Insert_FeeDetail_UnknownKind_IsRejected");
        yield return new TestCaseData(0, "@a_feeschedule", "0", "10").SetName("Insert_FeeDetail_ZeroBasis_IsRejected");
        yield return new TestCaseData(0, "@a_feeschedule", "1250", "0").SetName("Insert_FeeDetail_ZeroFee_IsRejected");
    }

    /// <summary>A fee the caller entered on an External account has no schedule (external-caller-numbers §6, script 0034).</summary>
    [Test]
    public async Task Insert_FeeDetail_BrokerageWithoutSchedule_Succeeds()
    {
        (await RunAsync(Detail(0, "NULL", "1250", "10") + "SELECT COUNT(*) FROM [TransactionFeeDetails] WHERE [CashLegId] = @a_feeleg;"))
            .ShouldBe(1);
    }

    [TestCaseSource(nameof(InvalidDetails))]
    public async Task Insert_InvalidFeeDetail_IsRejected(int kind, string schedule, string basis, string fee)
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync(Detail(kind, schedule, basis, fee)));

        exception.Number.ShouldBe(547);
    }

    [Test]
    public async Task Insert_DuplicateScheduleDate_IsRejected()
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync(Schedule("USD", "'2026-12-01'", "0", "NULL")));

        exception.Number.ShouldBe(2627);
    }

    [Test]
    public async Task Insert_ScheduleForAnotherCurrencyOrDate_IsAccepted()
    {
        var count = await RunAsync(Schedule("USD", "'2027-01-01'", "0", "NULL") + Schedule("NZD", "'2026-12-01'", "0", "5")
            + "SELECT COUNT(*) FROM [TradeFeeSchedules] WHERE [TenantId] = @a_tenant;");

        count.ShouldBe(3);
    }

    [TestCase("-1", "NULL")]
    [TestCase("0", "0")]
    [TestCase("10", "9")]
    public async Task Insert_ScheduleWithInvalidLimits_IsRejected(string minimum, string cap)
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync(Schedule("USD", "'2027-01-01'", minimum, cap)));

        exception.Number.ShouldBe(547);
    }

    [Test]
    public async Task Insert_ScheduleWithUnknownCurrency_IsRejected()
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync(Schedule("ZZZ", "'2027-01-01'", "0", "NULL")));

        exception.Number.ShouldBe(547);
    }

    [TestCase("-1", "0.5")]
    [TestCase("100", "-0.1")]
    [TestCase("100", "100.1")]
    public async Task Insert_InvalidBand_IsRejected(string from, string rate)
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync(Band(from, rate)));

        exception.Number.ShouldBe(547);
    }

    [Test]
    public async Task Insert_DuplicateBandFrom_IsRejected()
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync(Band("0", "0.3")));

        exception.Number.ShouldBe(2627);
    }

    [Test]
    public async Task Save_TradeFeeSchedule_RoundTripsValuesAndBands()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(SchemaDatabaseSetup.ConnectionString)
            .Options;
        Guid publicId;
        await using (var db = new ApplicationDbContext(options))
        {
            var (tenant, _) = await TwoTenantFixture.InsertAsync(db);
            var usd = Currency.Create("USD", "US Dollar", 2);
            var schedule = TradeFeeSchedule.Create(
                tenant.Id, usd, new DateOnly(2026, 12, 1), 10m, 250m,
                [new FeeBand(0m, 0.5m), new FeeBand(10000m, 0.3m)], new DateOnly(2026, 11, 15));
            schedule.CreatedBy = "test";
            db.TradeFeeSchedules.Add(schedule);
            await db.SaveChangesAsync();
            publicId = schedule.PublicId;
        }

        await using (var db = new ApplicationDbContext(options))
        {
            var loaded = await db.TradeFeeSchedules.AsNoTracking()
                .Include(schedule => schedule.BandRows)
                .SingleAsync(schedule => schedule.PublicId == publicId);

            loaded.QuoteCurrency.ShouldBe("USD");
            loaded.EffectiveFrom.ShouldBe(new DateOnly(2026, 12, 1));
            loaded.Minimum.ShouldBe(10m);
            loaded.Cap.ShouldBe(250m);
            loaded.Bands.ShouldBe([new FeeBand(0m, 0.5m), new FeeBand(10000m, 0.3m)]);
            loaded.RowVersion.ShouldNotBeEmpty();
        }
    }

    private static string Detail(int kind, string schedule, string basis, string fee) =>
        string.Format(FeeDetail, kind, schedule, basis, fee);

    private static string Schedule(string currency, string effectiveFrom, string minimum, string cap) => $"""
        INSERT INTO [TradeFeeSchedules] ([PublicId], [TenantId], [QuoteCurrency], [EffectiveFrom], [MinimumAmount], [CapAmount], [Created], [CreatedBy])
        VALUES (NEWID(), @a_tenant, '{currency}', {effectiveFrom}, {minimum}, {cap}, SYSDATETIMEOFFSET(), N'test');
        """;

    private static string Band(string from, string rate) => $"""
        INSERT INTO [TradeFeeBands] ([TenantId], [TradeFeeScheduleId], [FromAmount], [RatePercent])
        VALUES (@a_tenant, @a_feeschedule, {from}, {rate});
        """;

    /// <summary>Runs <paramref name="statement"/> after one tenant's rows and fee rows, then rolls back.</summary>
    private static async Task<object?> RunAsync(string statement)
    {
        await using var connection = new SqlConnection(SchemaDatabaseSetup.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                ScratchSchemaDatabase.TenantRows("a", Guid.NewGuid().ToString("N")[..12])
                + ScratchSchemaDatabase.FeeRows("a")
                + statement;
            return await command.ExecuteScalarAsync();
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    private static async Task<List<string>> Columns(string table)
    {
        var rows = await ScratchSchemaDatabase.ReadAsync(SchemaDatabaseSetup.ConnectionString, $"""
            SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = N'{table}' ORDER BY ORDINAL_POSITION
            """);
        return rows.Select(row => (string)row[0]).ToList();
    }
}
