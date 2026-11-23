using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Migrations;
using MyWealthV2.Domain.ValueObjects;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Schema;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

/// <summary>Migration batches §6: the batch tables, the migration detail of a Transaction, the batch arm of a matched
/// statement line, source kind 9, match method 6 and reconciliation item kind 19 (script 0054).</summary>
public class MigrationBatchesSchemaTests
{
    private const string Script = "0054_migration_batches.sql";

    [Test]
    public void SchemaDirectory_Includes0054Script()
    {
        new FileSchemaScriptSource(ScratchSchemaDatabase.SchemaDirectory()).GetScripts()
            .Select(script => script.Name)
            .ShouldContain(Script);
    }

    [TestCase("0")]
    [TestCase("1")]
    [TestCase("2")]
    [TestCase("3")]
    public async Task ABatch_InEveryStatus_WithItsColumns_Inserts(string status)
    {
        (await RunAsync($"""
            {World()}
            {Batch(status)}
            SELECT [Status] FROM [MigrationBatches] WHERE [Id] = @batch;
            """)).ShouldBe(int.Parse(status));
    }

    [TestCase("status: 4", "CK_MigrationBatches_Status")]
    [TestCase("status: 1, submitted: 0", "CK_MigrationBatches_Submitted")]
    [TestCase("status: 0, submitted: 1", "CK_MigrationBatches_Submitted")]
    [TestCase("status: 2, poster: @a_adviser", "CK_MigrationBatches_Posted")]
    [TestCase("status: 1, poster: @a_customer", "CK_MigrationBatches_Posted")]
    [TestCase("status: 0, cancelled: 1", "CK_MigrationBatches_Cancelled")]
    [TestCase("status: 0, returned: half", "CK_MigrationBatches_Returned")]
    [TestCase("status: 0, accountCount: -1", "CK_MigrationBatches_ControlAccountCount")]
    public async Task ABatchOutsideTheChecks_Violates(string change, string constraint)
    {
        var values = Values(change);
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {Batch(values["status"],
                submitted: values.TryGetValue("submitted", out var submitted) ? submitted == "1" : null,
                poster: values.GetValueOrDefault("poster"),
                cancelled: values.TryGetValue("cancelled", out var cancelled) ? cancelled == "1" : null,
                returned: values.GetValueOrDefault("returned", "none"),
                accountCount: values.GetValueOrDefault("accountCount", "2"))}
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain(constraint);
    }

    [Test]
    public async Task ControlTotals_OfEachKind_Insert()
    {
        (await RunAsync($"""
            {World()}
            {Batch("0")}
            {ControlTotal("0", "'USD'", "NULL", "250000")}
            {ControlTotal("1", "NULL", "@a_instrument", "1200.5")}
            {ControlTotal("2", "'USD'", "NULL", "0")}
            SELECT COUNT(*) FROM [MigrationBatchControlTotals] WHERE [MigrationBatchId] = @batch;
            """)).ShouldBe(3);
    }

    [TestCase("3", "'USD'", "NULL", "1", "CK_MigrationBatchControlTotals_Kind")]
    [TestCase("0", "NULL", "@a_instrument", "1", "CK_MigrationBatchControlTotals_Target")]
    [TestCase("1", "'USD'", "NULL", "1", "CK_MigrationBatchControlTotals_Target")]
    [TestCase("2", "'USD'", "@a_instrument", "1", "CK_MigrationBatchControlTotals_Target")]
    [TestCase("0", "'USD'", "NULL", "-1", "CK_MigrationBatchControlTotals_Amount")]
    public async Task AControlTotalOutsideTheChecks_Violates(string kind, string currency, string instrument, string amount, string constraint)
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {Batch("0")}
            {ControlTotal(kind, currency, instrument, amount)}
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain(constraint);
    }

    [Test]
    public async Task TwoControlTotalsOfOneCurrency_Violate()
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {Batch("0")}
            {ControlTotal("0", "'USD'", "NULL", "1")}
            {ControlTotal("0", "'USD'", "NULL", "2")}
            """));

        exception.Number.ShouldBe(2627);
        exception.Message.ShouldContain("UQ_MigrationBatchControlTotals_MigrationBatchId_Kind_Currency_InstrumentId");
    }

    [Test]
    public async Task AnEntryWithCashAndAHolding_Inserts()
    {
        (await RunAsync($"""
            {World()}
            {Batch("0")}
            {Entry("0", "@a_brokerage", "150000")}
            {Holding("@a_instrument", "800", "176000")}
            {Holding("@a_other", "1", "0")}
            SELECT COUNT(*) FROM [MigrationBatchHoldings] WHERE [MigrationBatchEntryId] = (SELECT [Id] FROM #entry);
            """)).ShouldBe(2);
    }

    [Test]
    public async Task TwoEntriesOfOneAccount_Violate()
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {Batch("0")}
            {Entry("0", "@a_brokerage", "1")}
            {Entry("1", "@a_brokerage", "2")}
            """));

        exception.Number.ShouldBe(2627);
        exception.Message.ShouldContain("UQ_MigrationBatchEntries_MigrationBatchId_AccountId");
    }

    [Test]
    public async Task TwoEntriesInOnePosition_Violate()
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {Batch("0")}
            {Entry("0", "@a_brokerage", "1")}
            {Entry("0", "@a_bank", "2")}
            """));

        exception.Number.ShouldBe(2627);
        exception.Message.ShouldContain("UQ_MigrationBatchEntries_MigrationBatchId_Position");
    }

    [Test]
    public async Task AnEntryWithCash0_Violates()
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {Batch("0")}
            {Entry("0", "@a_brokerage", "0")}
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain("CK_MigrationBatchEntries_CashAmount");
    }

    [Test]
    public async Task TwoHoldingsOfOneInstrumentInAnEntry_Violate()
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {Batch("0")}
            {Entry("0", "@a_brokerage", "NULL")}
            {Holding("@a_instrument", "1", "1")}
            {Holding("@a_instrument", "2", "2")}
            """));

        exception.Number.ShouldBe(2627);
        exception.Message.ShouldContain("UQ_MigrationBatchHoldings_MigrationBatchEntryId_InstrumentId");
    }

    [TestCase("0", "1", "CK_MigrationBatchHoldings_Quantity")]
    [TestCase("1", "-1", "CK_MigrationBatchHoldings_Cost")]
    public async Task AHoldingOutsideTheChecks_Violates(string quantity, string cost, string constraint)
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {Batch("0")}
            {Entry("0", "@a_brokerage", "NULL")}
            {Holding("@a_instrument", quantity, cost)}
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain(constraint);
    }

    [Test]
    public async Task ALineMatchedToABatch_Inserts_WithMethod6()
    {
        (await RunAsync($"""
            {World()}
            {Batch("0")}
            {Line("1", "6", batch: "@batch")}
            SELECT [MatchMethod] FROM [BankStatementLines] WHERE [Id] = SCOPE_IDENTITY();
            """)).ShouldBe(6);
    }

    [Test]
    public async Task ALineMatchedToABatchAndAnAccount_Violates()
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {Batch("0")}
            {Line("1", "6", batch: "@batch", account: "@a_bank", transaction: "@a_buy")}
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain("CK_BankStatementLines_Match");
    }

    [Test]
    public async Task AnUnmatchedLineWithABatch_Violates()
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {Batch("0")}
            {Line("2", "NULL", batch: "@batch", unmatchedReason: "0")}
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain("CK_BankStatementLines_Match");
    }

    [Test]
    public async Task ALineOfAnotherTenantsBatch_Violates()
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {ScratchSchemaDatabase.TenantRows("b", Guid.NewGuid().ToString("N")[..12])}
            {Batch("0", tenant: "b")}
            {Line("1", "6", batch: "@batch")}
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain("FK_BankStatementLines_MigrationBatches_TenantId_MigrationBatchId");
    }

    [Test]
    public async Task SourceKind9_NeedsASourcePublicId()
    {
        (await RunAsync($"""
            {World()}
            {InsertTransaction(9, "NEWID()")}
            SELECT [SourceKind] FROM [Transactions] WHERE [Id] = SCOPE_IDENTITY();
            """)).ShouldBe(9);

        var noId = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertTransaction(9, "NULL")}
            """));
        noId.Number.ShouldBe(547);
        noId.Message.ShouldContain("CK_Transactions_SourcePublicId");
    }

    [Test]
    public async Task AMigrationDetail_IsOnePerTransaction()
    {
        (await RunAsync($"""
            {World()}
            {InsertTransaction(9, "NEWID()")}
            DECLARE @opening int = SCOPE_IDENTITY();
            {Detail("@opening")}
            SELECT [PreviousProvider] FROM [TransactionMigrationDetails] WHERE [TransactionId] = @opening;
            """)).ShouldBe("Acme Wealth");

        var twice = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertTransaction(9, "NEWID()")}
            DECLARE @opening int = SCOPE_IDENTITY();
            {Detail("@opening")}
            {Detail("@opening")}
            """));
        twice.Number.ShouldBe(2627);
    }

    [Test]
    public async Task AnItemOfKind19_CarriesItsLine_AndOneWithout_Violates()
    {
        (await RunAsync($"""
            {World()}
            {Batch("0")}
            {Line("1", "6", batch: "@batch")}
            DECLARE @line int = SCOPE_IDENTITY();
            {Recon()}
            INSERT INTO [BankReconciliationItems]
                ([TenantId], [ReconciliationId], [Position], [Kind], [Class], [Amount], [Count], [StatementLineId], [Direction], [ValueDate], [LineStatus])
            VALUES (@a_tenant, @run, 1, 19, 1, 250000, 1, @line, 0, '2026-11-23', 1);
            SELECT [Kind] FROM [BankReconciliationItems] WHERE [ReconciliationId] = @run;
            """)).ShouldBe(19);

        var withoutLine = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {Recon()}
            INSERT INTO [BankReconciliationItems] ([TenantId], [ReconciliationId], [Position], [Kind], [Class], [Amount], [Count])
            VALUES (@a_tenant, @run, 1, 19, 1, 250000, 1);
            """));
        withoutLine.Number.ShouldBe(547);
        withoutLine.Message.ShouldContain("CK_BankReconciliationItems_Line");
    }

    [Test]
    public async Task EfRoundTrip_KeepsABatch_ItsControlTotals_EntriesAndHoldings_AndAMigrationDetail()
    {
        await using var db = await ScratchSchemaDatabase.CreateAsync("MigrationBatchesEf");
        await db.ApplyAllAsync();
        var row = (await db.ReadAsync($"""
            {ScratchSchemaDatabase.TenantRows("a", Guid.NewGuid().ToString("N")[..12])}
            SELECT @a_tenant, @a_brokerage, @a_bank, @a_instrument, @a_other, @a_adviser, @a_customer;
            """)).Single();
        var ids = row.Select(Convert.ToInt32).ToArray();
        var (tenantId, brokerageId, bankId, instrumentId, otherId, submitter, poster) =
            (ids[0], ids[1], ids[2], ids[3], ids[4], ids[5], ids[6]);
        var at = new DateTimeOffset(2026, 11, 24, 2, 15, 0, TimeSpan.Zero);
        var totals = new MigrationControlTotals(
            2,
            [new MigrationCurrencyAmount("USD", 250000m)],
            [new MigrationInstrumentQuantity(instrumentId, 1200.5m)],
            [new MigrationCurrencyAmount("USD", 264000m)]);
        int batchId;
        await using (var context = OpenDb(db.ConnectionString))
        {
            var batch = MigrationBatch.Create(tenantId, new MigrationBatchHeader("Acme", "Acme Wealth", new DateOnly(2026, 11, 20)), totals,
            [
                new MigrationEntrySpec(brokerageId, "ACME-1", 150000m,
                    [new MigrationHoldingSpec(instrumentId, 800m, 176000m), new MigrationHoldingSpec(otherId, 1m, 0m)]),
                new MigrationEntrySpec(bankId, null, 100000m, [])
            ]);
            batch.CreatedBy = "test";
            context.Set<MigrationBatch>().Add(batch);
            await context.SaveChangesAsync();
            batchId = batch.Id;
        }

        await using (var context = OpenDb(db.ConnectionString))
        {
            var batch = await context.Set<MigrationBatch>()
                .Include(item => item.Entries).ThenInclude(entry => entry.Holdings)
                .Include(item => item.ControlTotalRows)
                .SingleAsync(item => item.Id == batchId);
            batch.RowVersion.ShouldNotBeEmpty();
            batch.Replace(new MigrationBatchHeader("Acme", "Acme Wealth", new DateOnly(2026, 11, 20)), totals,
            [
                new MigrationEntrySpec(bankId, null, 100000m, []),
                new MigrationEntrySpec(brokerageId, "ACME-1", 150000m, [new MigrationHoldingSpec(instrumentId, 800m, 176000m)])
            ]);
            await context.SaveChangesAsync();
            batch.Submit(submitter, at);
            var opening = Transaction.PostMigratedCash(tenantId, bankId, "USD", 100000m, at, null,
                TransactionSource.Migration(batch.PublicId), new MigrationOrigin(new DateOnly(2026, 11, 20), "Acme Wealth"));
            opening.CreatedBy = "test";
            context.Set<Transaction>().Add(opening);
            await context.SaveChangesAsync();
            batch.Entries[0].RecordTransactions(opening.Id, null);
            batch.MarkPosted(poster, at.AddMinutes(1));
            await context.SaveChangesAsync();
        }

        await using (var context = OpenDb(db.ConnectionString))
        {
            var stored = await context.Set<MigrationBatch>().AsNoTracking()
                .Include(item => item.Entries).ThenInclude(entry => entry.Holdings)
                .Include(item => item.ControlTotalRows)
                .SingleAsync(item => item.Id == batchId);
            (stored.Status, stored.SubmittedByUserId, stored.PostedByUserId)
                .ShouldBe((MigrationBatchStatus.Posted, (int?)submitter, (int?)poster));
            stored.ControlTotals.ShouldBeEquivalentTo(totals);
            stored.Entries.OrderBy(e => e.Position).Select(e => (e.Position, e.AccountId, e.Cash, e.Holdings.Count))
                .ShouldBe([(0, bankId, (decimal?)100000m, 0), (1, brokerageId, (decimal?)150000m, 1)]);
            var entry = stored.Entries.Single(e => e.Position == 0);
            entry.OpeningTransactionId.ShouldNotBeNull();
            var transaction = await context.Set<Transaction>().AsNoTracking().Include(t => t.MigrationDetail)
                .SingleAsync(t => t.Id == entry.OpeningTransactionId);
            (transaction.Source.Kind, transaction.MigrationDetail!.AsOfDate, transaction.MigrationDetail.PreviousProvider)
                .ShouldBe((TransactionSourceKind.Migration, new DateOnly(2026, 11, 20), "Acme Wealth"));
        }
    }

    private static Dictionary<string, string> Values(string change) =>
        change.Split(", ").Select(part => part.Split(": ")).ToDictionary(pair => pair[0], pair => pair[1]);

    /// <summary>A tenant (<c>@a_*</c>) with a USD client money account <c>@cma</c>, and <c>#entry</c>.</summary>
    private static string World() => $"""
        {ScratchSchemaDatabase.TenantRows("a", Guid.NewGuid().ToString("N")[..12])}
        INSERT INTO [ClientMoneyAccounts]
            ([PublicId], [TenantId], [Currency], [BankName], [AccountName], [AccountNumber], [IsActive], [Created], [CreatedBy], [LastModified], [BankConnection])
        VALUES (NEWID(), @a_tenant, 'USD', N'Simulator Bank', N'Trust', N'N-{Guid.NewGuid().ToString("N")[..12]}', 1,
                SYSDATETIMEOFFSET(), N'test', SYSDATETIMEOFFSET(), N'simulator');
        DECLARE @cma int = SCOPE_IDENTITY();
        CREATE TABLE #entry ([Id] int);
        """;

    /// <summary>A batch <c>@batch</c> in <paramref name="status"/>, with the columns that status needs unless a
    /// parameter says otherwise. <c>@a_adviser</c> submits and <c>@a_customer</c> posts.</summary>
    private static string Batch(
        string status,
        bool? submitted = null,
        string? poster = null,
        bool? cancelled = null,
        string returned = "none",
        string accountCount = "2",
        string tenant = "a")
    {
        var hasSubmitted = submitted ?? status is "1" or "2";
        var postedBy = poster ?? (status == "2" ? $"@{tenant}_customer" : "NULL");
        var hasCancelled = cancelled ?? status == "3";
        var (returnedBy, returnedAt, returnNote) = returned switch
        {
            "half" => ($"@{tenant}_adviser", "NULL", "NULL"),
            "all" => ($"@{tenant}_adviser", "SYSDATETIMEOFFSET()", "N'Wrong totals'"),
            _ => ("NULL", "NULL", "NULL")
        };
        return $"""
            INSERT INTO [MigrationBatches]
                ([PublicId], [TenantId], [Status], [Name], [PreviousProvider], [AsOfDate], [ControlAccountCount],
                 [SubmittedByUserId], [SubmittedAt], [ReturnedByUserId], [ReturnedAt], [ReturnNote],
                 [PostedByUserId], [PostedAt], [CancelledByUserId], [CancelledAt], [CancelNote], [Created], [CreatedBy])
            VALUES (NEWID(), @{tenant}_tenant, {status}, N'Acme', N'Acme Wealth', '2026-11-20', {accountCount},
                    {(hasSubmitted ? $"@{tenant}_adviser" : "NULL")}, {(hasSubmitted ? "SYSDATETIMEOFFSET()" : "NULL")},
                    {returnedBy}, {returnedAt}, {returnNote},
                    {postedBy}, {(postedBy == "NULL" ? "NULL" : "SYSDATETIMEOFFSET()")},
                    {(hasCancelled ? $"@{tenant}_adviser" : "NULL")}, {(hasCancelled ? "SYSDATETIMEOFFSET()" : "NULL")},
                    {(hasCancelled ? "N'Duplicate'" : "NULL")}, SYSDATETIMEOFFSET(), N'test');
            DECLARE @batch int = SCOPE_IDENTITY();
            """;
    }

    private static string ControlTotal(string kind, string currency, string instrument, string amount) => $"""
        INSERT INTO [MigrationBatchControlTotals] ([TenantId], [MigrationBatchId], [Kind], [Currency], [InstrumentId], [Amount])
        VALUES (@a_tenant, @batch, {kind}, {currency}, {instrument}, {amount});
        """;

    /// <summary>An entry of <c>@batch</c>; <c>#entry</c> holds the Id of the last entry inserted.</summary>
    private static string Entry(string position, string account, string cash) => $"""
        INSERT INTO [MigrationBatchEntries] ([TenantId], [MigrationBatchId], [Position], [AccountId], [ExternalReference], [CashAmount])
        VALUES (@a_tenant, @batch, {position}, {account}, N'ACME-1', {cash});
        DELETE FROM #entry; INSERT INTO #entry VALUES (SCOPE_IDENTITY());
        """;

    private static string Holding(string instrument, string quantity, string cost) => $"""
        INSERT INTO [MigrationBatchHoldings] ([TenantId], [MigrationBatchEntryId], [InstrumentId], [Quantity], [Cost])
        SELECT @a_tenant, [Id], {instrument}, {quantity}, {cost} FROM #entry;
        """;

    private static string Line(
        string status,
        string method,
        string batch = "NULL",
        string unmatchedReason = "NULL",
        string account = "NULL",
        string transaction = "NULL") => $"""
        INSERT INTO [BankStatementLines]
            ([PublicId], [TenantId], [ClientMoneyAccountId], [Connection], [OutsideId], [ValueDate], [Direction], [Amount], [Currency],
             [ReceivedAt], [Status], [UnmatchedReason], [MatchMethod], [MatchedAt], [MatchedAccountId], [MatchedTransactionId],
             [MigrationBatchId], [Created], [CreatedBy])
        VALUES (NEWID(), @a_tenant, @cma, N'simulator', CONVERT(nvarchar(100), NEWID()), '2026-11-23', 0, 250000, 'USD',
                SYSDATETIMEOFFSET(), {status}, {unmatchedReason}, {method}, {(method == "NULL" ? "NULL" : "SYSDATETIMEOFFSET()")},
                {account}, {transaction}, {batch}, SYSDATETIMEOFFSET(), N'test');
        """;

    private static string InsertTransaction(int sourceKind, string sourcePublicId) => $"""
        INSERT INTO [Transactions] ([PublicId], [TenantId], [Type], [SourceKind], [SourcePublicId], [BookedAt], [Created], [CreatedBy])
        VALUES (NEWID(), @a_tenant, 0, {sourceKind}, {sourcePublicId}, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'test');
        """;

    private static string Detail(string transaction) => $"""
        INSERT INTO [TransactionMigrationDetails] ([TransactionId], [TenantId], [AsOfDate], [PreviousProvider])
        VALUES ({transaction}, @a_tenant, '2026-11-20', N'Acme Wealth');
        """;

    private static string Recon() => """
        INSERT INTO [BankReconciliations]
            ([PublicId], [TenantId], [ClientMoneyAccountId], [Currency], [Trigger], [RanAt], [RanByUserId], [Status],
             [BankBalance], [BalanceAt], [OpeningBalance], [StatementLinesTotal], [Difference], [CustodialCash],
             [BreakCount], [BreakTotal], [TimingCount], [TimingTotal], [KnownCount], [KnownTotal], [ReservedCash],
             [PaymentsInFlightCount], [PaymentsInFlightAmount], [Created], [CreatedBy])
        VALUES (NEWID(), @a_tenant, @cma, 'USD', 0, SYSDATETIMEOFFSET(), NULL, 0,
                250000, SYSDATETIMEOFFSET(), 0, 250000, 250000, 0, 0, 0, 1, 250000, 0, 0, 0, 0, 0, SYSDATETIMEOFFSET(), N'test');
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
