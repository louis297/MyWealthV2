using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.ValueObjects;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Schema;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

/// <summary>Interest run §6.2: the interest tables and the widened checks (script 0048).</summary>
[Parallelizable]
public class InterestSchemaTests
{
    private const string Script = "0048_interest.sql";

    [Test]
    public void SchemaDirectory_Includes0048Script()
    {
        new FileSchemaScriptSource(ScratchSchemaDatabase.SchemaDirectory()).GetScripts()
            .Select(script => script.Name)
            .ShouldContain(Script);
    }

    [Test]
    public async Task AScheduleWithBands_Inserts_AndASecondOfTheSameDay_ViolatesTheKey()
    {
        (await RunAsync($"""
            {World()}
            SELECT COUNT(*) FROM [InterestRateBands] WHERE [InterestRateScheduleId] = @schedule;
            """)).ShouldBe(1);

        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            INSERT INTO [InterestRateSchedules] ([PublicId], [TenantId], [Currency], [EffectiveFrom], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, 'USD', '2026-12-01', SYSDATETIMEOFFSET(), N'test');
            """));
        exception.Number.ShouldBe(2627);
        exception.Message.ShouldContain("UQ_InterestRateSchedules_TenantId_Currency_EffectiveFrom");
    }

    [TestCase("-1", "3", "CK_InterestRateBands_FromAmount")]
    [TestCase("1000", "100.0001", "CK_InterestRateBands_RatePercent")]
    [TestCase("1000", "-0.5", "CK_InterestRateBands_RatePercent")]
    public async Task ABandOutsideTheChecks_Violates(string from, string rate, string constraint)
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            INSERT INTO [InterestRateBands] ([TenantId], [InterestRateScheduleId], [FromAmount], [RatePercent])
            VALUES (@a_tenant, @schedule, {from}, {rate});
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain(constraint);
    }

    [Test]
    public async Task AnAccount_KeepsTheDayItWasAccruedThrough()
    {
        (await RunAsync($"""
            {World()}
            UPDATE [Accounts] SET [InterestAccruedThrough] = '2026-12-31' WHERE [Id] = @a_bank;
            SELECT CONVERT(nvarchar(10), [InterestAccruedThrough], 23) FROM [Accounts] WHERE [Id] = @a_bank;
            """)).ShouldBe("2026-12-31");
    }

    [Test]
    public async Task ASecondAccrualOfOneAccountAndDay_ViolatesTheKey()
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertAccrual("2026-12-01")}
            {InsertAccrual("2026-12-01")}
            """));

        exception.Number.ShouldBe(2627);
        exception.Message.ShouldContain("UQ_InterestAccruals_AccountId_AccrualDate");
    }

    [TestCase("0", "0.41095890", "CK_InterestAccruals_Balance")]
    [TestCase("5000", "0", "CK_InterestAccruals_Amount")]
    public async Task AnAccrualOutsideTheChecks_Violates(string balance, string amount, string constraint)
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertAccrual("2026-12-01", balance, amount)}
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain(constraint);
    }

    [Test]
    public async Task AnAccrual_IsStampedWithTheTransactionThatPaysIt()
    {
        (await RunAsync($"""
            {World()}
            {InsertAccrual("2026-12-01")}
            UPDATE [InterestAccruals] SET [PostedTransactionId] = @a_buy WHERE [AccountId] = @a_bank;
            SELECT [Amount] FROM [InterestAccruals] WHERE [PostedTransactionId] = @a_buy;
            """)).ShouldBe(0.41095890m);
    }

    [TestCase(0, false, false)]
    [TestCase(1, true, false)]
    [TestCase(2, true, true)]
    public async Task ARunOfTheShape_Inserts(int trigger, bool ranBy, bool account)
    {
        (await RunAsync($"""
            {World()}
            {InsertRun(trigger, ranBy, account)}
            SELECT [Trigger] FROM [InterestRuns] WHERE [Id] = SCOPE_IDENTITY();
            """)).ShouldBe(trigger);
    }

    [TestCase(0, true, false, "CK_InterestRuns_RanBy")]
    [TestCase(1, false, false, "CK_InterestRuns_RanBy")]
    [TestCase(2, true, false, "CK_InterestRuns_Account")]
    [TestCase(1, true, true, "CK_InterestRuns_Account")]
    [TestCase(3, true, false, "CK_InterestRuns_Trigger")]
    public async Task ARunOutsideTheChecks_Violates(int trigger, bool ranBy, bool account, string constraint)
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertRun(trigger, ranBy, account)}
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain(constraint);
    }

    [Test]
    public async Task ACashLegOfRoleTax_IsAllowed_AndRole4Violates()
    {
        (await RunAsync($"""
            {World()}
            {InsertLeg(2)}
            SELECT [Role] FROM [TransactionCashLegs] WHERE [Id] = @leg;
            """)).ShouldBe(2);

        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertLeg(4)}
            """));
        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain("CK_TransactionCashLegs_Role");
    }

    [Test]
    public async Task ASecondTaxDetailOfTheSameKindOnATransaction_ViolatesTheKey()
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertLeg(2)}
            DECLARE @first int = @leg;
            {InsertTaxDetail("@first", 0)}
            {InsertLeg(2, declare: false)}
            {InsertTaxDetail("@leg", 0)}
            """));

        exception.Number.ShouldBe(2627);
        exception.Message.ShouldContain("UQ_TransactionTaxDetails_TransactionId_Kind");
    }

    [TestCase("3", "17.5", "2.16", "CK_TransactionTaxDetails_Kind")]
    [TestCase("0", "0", "2.16", "CK_TransactionTaxDetails_RatePercent")]
    [TestCase("0", "17.5", "0", "CK_TransactionTaxDetails_TaxAmount")]
    public async Task ATaxDetailOutsideTheChecks_Violates(string kind, string rate, string tax, string constraint)
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertLeg(2)}
            INSERT INTO [TransactionTaxDetails]
                ([CashLegId], [TenantId], [TransactionId], [Kind], [RatePercent], [BasisAmount], [TaxAmount], [Currency])
            VALUES (@leg, @a_tenant, @a_buy, {kind}, {rate}, 12.33, {tax}, 'USD');
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain(constraint);
    }

    [TestCase("'2026-12-01'", "'2026-12-31'", 31, true, null)]
    [TestCase("'2026-12-31'", "'2026-12-01'", 31, false, "CK_TransactionInterestDetails_Period")]
    [TestCase("'2026-12-01'", "'2026-12-31'", 0, false, "CK_TransactionInterestDetails_Days")]
    public async Task AnInterestDetail_OfTheShape_InsertsOnce(string from, string to, int days, bool inserts, string? constraint)
    {
        var statement = $"""
            {World()}
            INSERT INTO [TransactionInterestDetails] ([TransactionId], [TenantId], [PeriodFrom], [PeriodTo], [Days], [AccruedAmount], [GrossAmount])
            VALUES (@a_buy, @a_tenant, {from}, {to}, {days}, 12.32876700, 12.33);
            SELECT [Days] FROM [TransactionInterestDetails] WHERE [TransactionId] = @a_buy;
            """;
        if (inserts)
        {
            (await RunAsync(statement)).ShouldBe(days);
            return;
        }

        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync(statement));
        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain(constraint!);
    }

    [Test]
    public async Task SourceKind6_NeedsASourcePublicId()
    {
        (await RunAsync($"""
            {World()}
            {InsertTransaction(6, "NEWID()")}
            SELECT [SourceKind] FROM [Transactions] WHERE [Id] = SCOPE_IDENTITY();
            """)).ShouldBe(6);

        var noId = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertTransaction(6, "NULL")}
            """));
        noId.Number.ShouldBe(547);
        noId.Message.ShouldContain("CK_Transactions_SourcePublicId");

        // Kind 7 (CorporateAction) and the upper bound are covered by CorporateActionsSchemaTests (script 0050).
    }

    [Test]
    public async Task AStatementLine_IsATransferByDefault_KindInterestIsAllowed_And2Violates()
    {
        (await RunAsync($"""
            {World()}
            {InsertLine("NULL")}
            SELECT [Kind] FROM [BankStatementLines] WHERE [Id] = @line;
            """)).ShouldBe(0);
        (await RunAsync($"""
            {World()}
            {InsertLine("1")}
            SELECT [Kind] FROM [BankStatementLines] WHERE [Id] = @line;
            """)).ShouldBe(1);

        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertLine("2")}
            """));
        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain("CK_BankStatementLines_Kind");
    }

    [Test]
    public async Task ABankInterestItem_CarriesItsLine_AndTheLedgerKindsDoNot()
    {
        (await RunAsync($"""
            {World()}
            {InsertRecon()}
            {InsertLine("1")}
            INSERT INTO [BankReconciliationItems]
                ([TenantId], [ReconciliationId], [Position], [Kind], [Class], [Amount], [Count], [StatementLineId], [Direction],
                 [ValueDate], [LineStatus], [Note])
            VALUES (@a_tenant, @run, 1, 12, 2, 520, 1, @line, 0, '2026-12-31', 3, N'Bank interest');
            INSERT INTO [BankReconciliationItems] ([TenantId], [ReconciliationId], [Position], [Kind], [Class], [Amount], [Count])
            VALUES (@a_tenant, @run, 2, 13, 2, -410.55, 25), (@a_tenant, @run, 3, 14, 2, 93.40, 25);
            SELECT COUNT(*) FROM [BankReconciliationItems] WHERE [ReconciliationId] = @run;
            """)).ShouldBe(3);
    }

    [TestCase(12, "CK_BankReconciliationItems_Line")]
    [TestCase(22, "CK_BankReconciliationItems_Kind")]
    public async Task AnItemOutsideTheChecks_Violates(int kind, string constraint)
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertRecon()}
            INSERT INTO [BankReconciliationItems] ([TenantId], [ReconciliationId], [Position], [Kind], [Class], [Amount], [Count])
            VALUES (@a_tenant, @run, 1, {kind}, 2, 500, 1);
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain(constraint);
    }

    [Test]
    public async Task EfRoundTrip_KeepsAScheduleAndItsBands()
    {
        await using var db = await ScratchSchemaDatabase.CreateAsync("InterestScheduleEf");
        await db.ApplyAllAsync();
        var tenantId = Convert.ToInt32((await db.ReadAsync($"""
            {ScratchSchemaDatabase.TenantRows("a", Guid.NewGuid().ToString("N")[..12])}
            SELECT @a_tenant;
            """)).Single()[0]);
        var usd = Currency.Create("USD", "US Dollar", 2);
        var schedule = InterestRateSchedule.Create(
            tenantId, usd, new DateOnly(2026, 12, 1), [new FeeBand(0m, 3m), new FeeBand(100000m, 2.5m)], new DateOnly(2026, 11, 22));
        schedule.CreatedBy = "test";
        await using (var context = OpenDb(db.ConnectionString))
        {
            context.Set<InterestRateSchedule>().Add(schedule);
            await context.SaveChangesAsync();
        }

        await using (var context = OpenDb(db.ConnectionString))
        {
            var stored = await context.Set<InterestRateSchedule>().Include(row => row.BandRows).SingleAsync(row => row.Id == schedule.Id);
            stored.RowVersion.ShouldNotBeEmpty();
            stored.Update(new DateOnly(2027, 1, 1), [new FeeBand(0m, 3.25m)], 2, new DateOnly(2026, 11, 22));
            await context.SaveChangesAsync();
        }

        await using (var context = OpenDb(db.ConnectionString))
        {
            var stored = await context.Set<InterestRateSchedule>().AsNoTracking().Include(row => row.BandRows)
                .SingleAsync(row => row.Id == schedule.Id);
            stored.PublicId.ShouldBe(schedule.PublicId);
            stored.TenantId.ShouldBe(tenantId);
            stored.Currency.ShouldBe("USD");
            stored.EffectiveFrom.ShouldBe(new DateOnly(2027, 1, 1));
            stored.Bands.ShouldBe([new FeeBand(0m, 3.25m)]);
        }
    }

    /// <summary>A tenant (<c>@a_*</c> from <see cref="ScratchSchemaDatabase.TenantRows"/>) with a USD client money
    /// account <c>@cma</c> and a USD schedule <c>@schedule</c> from 1 December 2026 with one band.</summary>
    private static string World()
    {
        var code = Guid.NewGuid().ToString("N")[..12];
        return $"""
            {ScratchSchemaDatabase.TenantRows("a", code)}
            INSERT INTO [ClientMoneyAccounts]
                ([PublicId], [TenantId], [Currency], [BankName], [AccountName], [AccountNumber], [IsActive], [Created], [CreatedBy], [LastModified], [BankConnection])
            VALUES (NEWID(), @a_tenant, 'USD', N'Simulator Bank', N'Trust', N'N-{code}', 1, SYSDATETIMEOFFSET(), N'test', SYSDATETIMEOFFSET(), N'simulator');
            DECLARE @cma int = SCOPE_IDENTITY();
            INSERT INTO [InterestRateSchedules] ([PublicId], [TenantId], [Currency], [EffectiveFrom], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, 'USD', '2026-12-01', SYSDATETIMEOFFSET(), N'test');
            DECLARE @schedule int = SCOPE_IDENTITY();
            INSERT INTO [InterestRateBands] ([TenantId], [InterestRateScheduleId], [FromAmount], [RatePercent])
            VALUES (@a_tenant, @schedule, 0, 3);
            """;
    }

    private static string InsertAccrual(string day, string balance = "5000", string amount = "0.41095890") => $"""
        INSERT INTO [InterestAccruals]
            ([TenantId], [AccountId], [AccrualDate], [Balance], [InterestRateScheduleId], [EffectiveRatePercent], [Amount])
        VALUES (@a_tenant, @a_bank, '{day}', {balance}, @schedule, 3, {amount});
        """;

    private static string InsertRun(int trigger, bool ranBy, bool account) => $"""
        INSERT INTO [InterestRuns]
            ([PublicId], [TenantId], [AsOfDate], [Trigger], [RanByUserId], [AccountId], [StartedAt], [AccountsProcessed],
             [AccountsFailed], [PostingCount], [Created], [CreatedBy])
        VALUES (NEWID(), @a_tenant, '2026-12-31', {trigger}, {(ranBy ? "@a_adviser" : "NULL")}, {(account ? "@a_bank" : "NULL")},
                SYSDATETIMEOFFSET(), 0, 0, 0, SYSDATETIMEOFFSET(), N'test');
        """;

    /// <summary>A cash leg <c>@leg</c> of <paramref name="role"/> on <c>@a_buy</c> and <c>@a_bank</c>.</summary>
    private static string InsertLeg(int role, bool declare = true) => $"""
        INSERT INTO [TransactionCashLegs] ([TenantId], [TransactionId], [AccountId], [Role], [Amount], [Currency], [Created], [CreatedBy])
        VALUES (@a_tenant, @a_buy, @a_bank, {role}, -2.16, 'USD', SYSDATETIMEOFFSET(), N'test');
        {(declare ? "DECLARE @leg int = SCOPE_IDENTITY();" : "SET @leg = SCOPE_IDENTITY();")}
        """;

    private static string InsertTaxDetail(string leg, int kind) => $"""
        INSERT INTO [TransactionTaxDetails] ([CashLegId], [TenantId], [TransactionId], [Kind], [RatePercent], [BasisAmount], [TaxAmount], [Currency])
        VALUES ({leg}, @a_tenant, @a_buy, {kind}, 17.5, 12.33, 2.16, 'USD');
        """;

    private static string InsertTransaction(int sourceKind, string sourcePublicId) => $"""
        INSERT INTO [Transactions] ([PublicId], [TenantId], [Type], [SourceKind], [SourcePublicId], [BookedAt], [Created], [CreatedBy])
        VALUES (NEWID(), @a_tenant, 3, {sourceKind}, {sourcePublicId}, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'test');
        """;

    /// <summary>A credit line <c>@line</c> of <c>@cma</c>, Ignored, with <paramref name="kind"/> (NULL: the default).</summary>
    private static string InsertLine(string kind)
    {
        var column = kind == "NULL" ? "" : ", [Kind]";
        var value = kind == "NULL" ? "" : $", {kind}";
        return $"""
            INSERT INTO [BankStatementLines]
                ([PublicId], [TenantId], [ClientMoneyAccountId], [Connection], [OutsideId], [ValueDate], [Direction], [Amount], [Currency],
                 [ReceivedAt], [Status], [Created], [CreatedBy]{column})
            VALUES (NEWID(), @a_tenant, @cma, N'simulator', CONVERT(nvarchar(100), NEWID()), '2026-12-31', 0, 520, 'USD',
                    SYSDATETIMEOFFSET(), 3, SYSDATETIMEOFFSET(), N'test'{value});
            DECLARE @line int = SCOPE_IDENTITY();
            """;
    }

    private static string InsertRecon() => """
        INSERT INTO [BankReconciliations]
            ([PublicId], [TenantId], [ClientMoneyAccountId], [Currency], [Trigger], [RanAt], [RanByUserId], [Status],
             [BankBalance], [BalanceAt], [OpeningBalance], [StatementLinesTotal], [Difference], [CustodialCash],
             [BreakCount], [BreakTotal], [TimingCount], [TimingTotal], [KnownCount], [KnownTotal], [ReservedCash],
             [PaymentsInFlightCount], [PaymentsInFlightAmount], [Created], [CreatedBy])
        VALUES (NEWID(), @a_tenant, @cma, 'USD', 0, SYSDATETIMEOFFSET(), NULL, 0,
                12512.4, SYSDATETIMEOFFSET(), 0, 12512.4, 1687.6, 10824.8, 0, 0, 0, 0, 0, 0, 0, 0, 0, SYSDATETIMEOFFSET(), N'test');
        DECLARE @run int = SCOPE_IDENTITY();
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
