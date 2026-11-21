using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Schema;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

/// <summary>Bank-reconciliation §6.1: snapshot isolation, the balance of a feed cursor, the stored runs and their
/// items, and the report scope <c>Tenant</c> (script 0045).</summary>
public class BankReconciliationsSchemaTests
{
    private const string Script = "0045_bank_reconciliations.sql";

    [Test]
    public void SchemaDirectory_Includes0045Script()
    {
        new FileSchemaScriptSource(ScratchSchemaDatabase.SchemaDirectory()).GetScripts()
            .Select(script => script.Name)
            .ShouldContain(Script);
    }

    [Test]
    public async Task Apply_TurnsOnSnapshotIsolation()
    {
        var state = (await ScratchSchemaDatabase.ReadAsync(SchemaDatabaseSetup.ConnectionString,
            "SELECT [snapshot_isolation_state] FROM sys.databases WHERE [name] = DB_NAME();")).Single()[0];

        Convert.ToInt32(state).ShouldBe(1);
    }

    [Test]
    public async Task Apply0045_LeavesAnExistingCursorWithoutABalance()
    {
        await using var db = await ScratchSchemaDatabase.CreateAsync("BankReconciliations0045");
        await db.ApplyBeforeAsync(Script);
        await db.ExecuteAsync($"""
            {World()}
            INSERT INTO [BankFeedCursors] ([ClientMoneyAccountId], [Cursor], [LastSucceededAt])
            VALUES (@cma, N'w:3', SYSDATETIMEOFFSET());
            """);

        await db.ApplyAllAsync();

        var row = (await db.ReadAsync("SELECT [Cursor], [Balance], [BalanceAt], [OpeningBalance] FROM [BankFeedCursors];"))
            .ShouldHaveSingleItem();
        row[0].ShouldBe("w:3");
        row[1].ShouldBe(DBNull.Value);
        row[2].ShouldBe(DBNull.Value);
        row[3].ShouldBe(DBNull.Value);
        var versions = await db.ReadAsync($"SELECT COUNT(*) FROM [SchemaVersions] WHERE [ScriptName] = N'{Script}';");
        ((int)versions.Single()[0]).ShouldBe(1);
    }

    [Test]
    public async Task ACursorWithABalanceAndAnOpeningBalance_IsStored()
    {
        (await RunAsync($"""
            {World()}
            INSERT INTO [BankFeedCursors] ([ClientMoneyAccountId], [Balance], [BalanceAt], [OpeningBalance])
            VALUES (@cma, -12.5, SYSDATETIMEOFFSET(), 0);
            SELECT [Balance] FROM [BankFeedCursors] WHERE [ClientMoneyAccountId] = @cma;
            """)).ShouldBe(-12.5m);
    }

    [Test]
    public async Task ACursorWithOnlyAnOpeningBalance_IsStored()
    {
        (await RunAsync($"""
            {World()}
            INSERT INTO [BankFeedCursors] ([ClientMoneyAccountId], [OpeningBalance]) VALUES (@cma, 100);
            SELECT [OpeningBalance] FROM [BankFeedCursors] WHERE [ClientMoneyAccountId] = @cma;
            """)).ShouldBe(100m);
    }

    [TestCase("100", "NULL", "0", "CK_BankFeedCursors_Balance")]
    [TestCase("NULL", "SYSDATETIMEOFFSET()", "0", "CK_BankFeedCursors_Balance")]
    [TestCase("100", "SYSDATETIMEOFFSET()", "NULL", "CK_BankFeedCursors_Opening")]
    public async Task ACursorOutsideItsBalanceChecks_IsRejected(string balance, string balanceAt, string opening, string check)
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            INSERT INTO [BankFeedCursors] ([ClientMoneyAccountId], [Balance], [BalanceAt], [OpeningBalance])
            VALUES (@cma, {balance}, {balanceAt}, {opening});
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain(check);
    }

    [TestCase(0, false, 2, false)]
    [TestCase(1, true, 2, false)]
    [TestCase(0, false, 0, true)]
    [TestCase(1, true, 1, true)]
    public async Task ARunWithItsTriggerAndStatusShape_IsStored(int trigger, bool ranBy, int status, bool bankFigures)
    {
        (await RunAsync($"""
            {World()}
            {InsertRun(trigger, ranBy, status, bankFigures)}
            SELECT [Status] FROM [BankReconciliations] WHERE [Id] = @run;
            """)).ShouldBe(status);
    }

    [TestCase(0, true)]
    [TestCase(1, false)]
    public async Task ARunWhosePersonDoesNotMatchItsTrigger_ViolatesTheRanByCheck(int trigger, bool ranBy)
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertRun(trigger, ranBy, 2, bankFigures: false)}
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain("CK_BankReconciliations_RanBy");
    }

    [TestCase(2, true)]
    [TestCase(0, false)]
    [TestCase(1, false)]
    public async Task ARunWhoseBankFiguresDoNotMatchItsStatus_ViolatesTheBankFiguresCheck(int status, bool bankFigures)
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertRun(0, false, status, bankFigures)}
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain("CK_BankReconciliations_BankFigures");
    }

    [Test]
    public async Task ARunWithOnlySomeBankFigures_ViolatesTheBankFiguresCheck()
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertRun(0, false, 0, bankFigures: true)}
            UPDATE [BankReconciliations] SET [OpeningBalance] = NULL WHERE [Id] = @run;
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain("CK_BankReconciliations_BankFigures");
    }

    [TestCase("[Trigger] = 2", "CK_BankReconciliations_Trigger")]
    [TestCase("[Status] = 3", "CK_BankReconciliations_Status")]
    [TestCase("[FeedLastFailure] = 7", "CK_BankReconciliations_FeedLastFailure")]
    public async Task ARunValueOutsideItsCheck_IsRejected(string set, string check)
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertRun(0, false, 2, bankFigures: false)}
            UPDATE [BankReconciliations] SET {set} WHERE [Id] = @run;
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain(check);
    }

    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(6)]
    public async Task ALineItem_CarriesTheLine(int kind)
    {
        (await RunAsync($"""
            {World()}
            {InsertRun(0, false, 1, bankFigures: true)}
            {InsertLine()}
            {InsertItem(kind, line: true)}
            SELECT [Kind] FROM [BankReconciliationItems] WHERE [ReconciliationId] = @run;
            """)).ShouldBe(kind);
    }

    [TestCase(2, false)]
    [TestCase(6, false)]
    [TestCase(0, true)]
    [TestCase(1, true)]
    [TestCase(5, true)]
    [TestCase(7, true)]
    public async Task AnItemWithALineOutsideALineKind_ViolatesTheLineCheck(int kind, bool line)
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertRun(0, false, 1, bankFigures: true)}
            {InsertLine()}
            {InsertItem(kind, line)}
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain("CK_BankReconciliationItems_Line");
    }

    [Test]
    public async Task ALineItemWithAReasonOnALineNotUnmatched_ViolatesTheReasonCheck()
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertRun(0, false, 1, bankFigures: true)}
            {InsertLine()}
            INSERT INTO [BankReconciliationItems]
                ([TenantId], [ReconciliationId], [Position], [Kind], [Class], [Amount], [Count], [StatementLineId], [Direction],
                 [ValueDate], [LineStatus], [UnmatchedReason])
            VALUES (@a_tenant, @run, 1, 6, 2, 5, 1, @line, 0, '2026-11-21', 3, 1);
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain("CK_BankReconciliationItems_UnmatchedReason");
    }

    [Test]
    public async Task AnOtherClientMoneyAccountItem_NamesTheAccount()
    {
        (await RunAsync($"""
            {World()}
            {InsertRun(0, false, 0, bankFigures: true)}
            INSERT INTO [BankReconciliationItems]
                ([TenantId], [ReconciliationId], [Position], [Kind], [Class], [Amount], [Count], [OtherClientMoneyAccountId])
            VALUES (@a_tenant, @run, 1, 11, 2, 5, 3, @cma);
            SELECT [Count] FROM [BankReconciliationItems] WHERE [ReconciliationId] = @run;
            """)).ShouldBe(3);
    }

    [TestCase(11, false)]
    [TestCase(10, true)]
    public async Task AnItemNamingAnotherAccountOutsideItsKind_ViolatesTheOtherAccountCheck(int kind, bool other)
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertRun(0, false, 0, bankFigures: true)}
            INSERT INTO [BankReconciliationItems]
                ([TenantId], [ReconciliationId], [Position], [Kind], [Class], [Amount], [Count], [OtherClientMoneyAccountId])
            VALUES (@a_tenant, @run, 1, {kind}, 2, 5, 1, {(other ? "@cma" : "NULL")});
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain("CK_BankReconciliationItems_OtherClientMoneyAccount");
    }

    [TestCase("[Kind] = 12", "CK_BankReconciliationItems_Kind")]
    [TestCase("[Class] = 3", "CK_BankReconciliationItems_Class")]
    [TestCase("[Position] = 0", "CK_BankReconciliationItems_Position")]
    public async Task AnItemValueOutsideItsCheck_IsRejected(string set, string check)
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertRun(0, false, 0, bankFigures: true)}
            {InsertItem(0, line: false)}
            UPDATE [BankReconciliationItems] SET {set} WHERE [ReconciliationId] = @run;
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain(check);
    }

    [Test]
    public async Task TwoItemsOfARunAtOnePosition_ViolateTheUniqueIndex()
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertRun(0, false, 0, bankFigures: true)}
            {InsertItem(0, line: false)}
            {InsertItem(1, line: false)}
            """));

        exception.Number.ShouldBe(2601);
        exception.Message.ShouldContain("UX_BankReconciliationItems_ReconciliationId_Position");
    }

    [Test]
    public async Task AReportOfScopeTenant_IsAccepted_AndAnUnknownScopeIsNot()
    {
        (await RunAsync("""
            INSERT INTO [Reports] ([PublicId], [Code], [Name], [Function], [Scope], [AllowedTriggers])
            VALUES (NEWID(), N'firm-test', N'Firm', N'Firm', N'Tenant', N'["manual"]');
            SELECT [Scope] FROM [Reports] WHERE [Code] = N'firm-test';
            """)).ShouldBe("Tenant");

        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync("""
            INSERT INTO [Reports] ([PublicId], [Code], [Name], [Function], [Scope], [AllowedTriggers])
            VALUES (NEWID(), N'firm-test', N'Firm', N'Firm', N'Firm', N'["manual"]');
            """));
        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain("CK_Reports_Scope");
    }

    [Test]
    public async Task EfRoundTrip_StoresTheBalanceAndTheOpeningBalanceOfACursor()
    {
        await using var db = await ScratchSchemaDatabase.CreateAsync("BankReconciliationsCursor");
        await db.ApplyAllAsync();
        await db.ExecuteAsync(World());
        var cmaId = (int)(await db.ReadAsync("SELECT [Id] FROM [ClientMoneyAccounts];")).Single()[0];
        var at = new DateTimeOffset(2026, 11, 22, 16, 59, 7, TimeSpan.Zero);

        await using (var context = OpenDb(db.ConnectionString))
        {
            var cursor = BankFeedCursor.Start(cmaId);
            cursor.StorePage("w:3", 1512.4m, 500m, at);
            context.BankFeedCursors.Add(cursor);
            await context.SaveChangesAsync();
        }

        await using (var context = OpenDb(db.ConnectionString))
        {
            var cursor = await context.BankFeedCursors.AsNoTracking().SingleAsync(row => row.ClientMoneyAccountId == cmaId);
            cursor.Balance.ShouldBe(1512.4m);
            cursor.BalanceAt.ShouldBe(at);
            cursor.OpeningBalance.ShouldBe(1012.4m);
        }
    }

    [Test]
    public async Task ASnapshotRead_DoesNotSeeALineAndItsDepositWrittenByAConcurrentTransaction_UntilBothAreCommitted()
    {
        // Bank-reconciliation R6: the run reads at SNAPSHOT, so a line's status and its Transaction are seen together.
        await using var db = await ScratchSchemaDatabase.CreateAsync("BankReconciliationsSnapshot");
        await db.ApplyAllAsync();
        await db.ExecuteAsync(World());

        await using var writer = new SqlConnection(db.ConnectionString);
        await writer.OpenAsync();
        await using var write = writer.BeginTransaction();
        await using (var command = writer.CreateCommand())
        {
            command.Transaction = write;
            command.CommandText = """
                DECLARE @tenant int = (SELECT [TenantId] FROM [ClientMoneyAccounts]);
                DECLARE @cma int = (SELECT [Id] FROM [ClientMoneyAccounts]);
                DECLARE @account int = (SELECT TOP 1 [Id] FROM [Accounts] WHERE [Currency] = 'USD');
                INSERT INTO [BankStatementLines]
                    ([PublicId], [TenantId], [ClientMoneyAccountId], [Connection], [OutsideId], [ValueDate], [Direction], [Amount], [Currency],
                     [ReceivedAt], [Status], [Created], [CreatedBy])
                VALUES (NEWID(), @tenant, @cma, N'simulator', N'snap-L1', '2026-11-21', 0, 500, 'USD', SYSDATETIMEOFFSET(), 0,
                        SYSDATETIMEOFFSET(), N'test');
                INSERT INTO [Transactions] ([PublicId], [TenantId], [Type], [SourceKind], [BookedAt], [Created], [CreatedBy])
                VALUES (NEWID(), @tenant, 0, 0, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'test');
                INSERT INTO [TransactionCashLegs] ([TenantId], [TransactionId], [AccountId], [Amount], [Currency], [Created], [CreatedBy])
                VALUES (@tenant, SCOPE_IDENTITY(), @account, 500, 'USD', SYSDATETIMEOFFSET(), N'test');
                """;
            await command.ExecuteNonQueryAsync();
        }

        var before = await ReadLinesAndLegsAsync(db.ConnectionString);
        await write.CommitAsync();
        var after = await ReadLinesAndLegsAsync(db.ConnectionString);

        before.ShouldBe((0, 0m));
        after.ShouldBe((1, 500m));
    }

    /// <summary>A tenant (<c>@a_*</c> from <see cref="ScratchSchemaDatabase.TenantRows"/>) with a USD client money
    /// account <c>@cma</c>.</summary>
    private static string World()
    {
        var code = Guid.NewGuid().ToString("N")[..12];
        return $"""
            {ScratchSchemaDatabase.TenantRows("a", code)}
            INSERT INTO [ClientMoneyAccounts]
                ([PublicId], [TenantId], [Currency], [BankName], [AccountName], [AccountNumber], [IsActive], [Created], [CreatedBy], [LastModified], [BankConnection])
            VALUES (NEWID(), @a_tenant, 'USD', N'Simulator Bank', N'Trust', N'N-{code}', 1, SYSDATETIMEOFFSET(), N'test', SYSDATETIMEOFFSET(), N'simulator');
            DECLARE @cma int = SCOPE_IDENTITY();
            """;
    }

    /// <summary>A run <c>@run</c> of <c>@cma</c>; <paramref name="ranBy"/> names <c>@a_adviser</c>.</summary>
    private static string InsertRun(int trigger, bool ranBy, int status, bool bankFigures)
    {
        var figures = bankFigures ? "12512.4, SYSDATETIMEOFFSET(), 0, 12512.4, 1687.6" : "NULL, NULL, NULL, NULL, NULL";
        return $"""
            INSERT INTO [BankReconciliations]
                ([PublicId], [TenantId], [ClientMoneyAccountId], [Currency], [Trigger], [RanAt], [RanByUserId], [Status],
                 [BankBalance], [BalanceAt], [OpeningBalance], [StatementLinesTotal], [Difference], [CustodialCash],
                 [BreakCount], [BreakTotal], [TimingCount], [TimingTotal], [KnownCount], [KnownTotal], [ReservedCash],
                 [PaymentsInFlightCount], [PaymentsInFlightAmount], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @cma, 'USD', {trigger}, SYSDATETIMEOFFSET(), {(ranBy ? "@a_adviser" : "NULL")}, {status},
                    {figures}, 10824.8, 0, 0, 0, 0, 0, 0, 0, 0, 0, SYSDATETIMEOFFSET(), N'test');
            DECLARE @run int = SCOPE_IDENTITY();
            """;
    }

    /// <summary>An Unmatched credit line <c>@line</c> of <c>@cma</c>.</summary>
    private static string InsertLine() => """
        INSERT INTO [BankStatementLines]
            ([PublicId], [TenantId], [ClientMoneyAccountId], [Connection], [OutsideId], [ValueDate], [Direction], [Amount], [Currency],
             [ReceivedAt], [Status], [UnmatchedReason], [Created], [CreatedBy])
        VALUES (NEWID(), @a_tenant, @cma, N'simulator', CONVERT(nvarchar(100), NEWID()), '2026-11-21', 0, 500, 'USD',
                SYSDATETIMEOFFSET(), 2, 1, SYSDATETIMEOFFSET(), N'test');
        DECLARE @line int = SCOPE_IDENTITY();
        """;

    /// <summary>An item at position 1 of <c>@run</c>; with <paramref name="line"/> it carries <c>@line</c>.</summary>
    private static string InsertItem(int kind, bool line) => line
        ? $"""
            INSERT INTO [BankReconciliationItems]
                ([TenantId], [ReconciliationId], [Position], [Kind], [Class], [Amount], [Count], [StatementLineId], [Direction],
                 [ValueDate], [LineStatus], [UnmatchedReason], [Note])
            VALUES (@a_tenant, @run, 1, {kind}, 0, 500, 1, @line, 0, '2026-11-21', 2, 1, N'Note');
            """
        : $"""
            INSERT INTO [BankReconciliationItems] ([TenantId], [ReconciliationId], [Position], [Kind], [Class], [Amount], [Count])
            VALUES (@a_tenant, @run, 1, {kind}, 0, 500, 1);
            """;

    /// <summary>Counts the lines and sums the cash legs in one SNAPSHOT transaction, as a reconciliation reads; a
    /// read that waits for the writer's locks fails after a short timeout.</summary>
    private static async Task<(int Lines, decimal Legs)> ReadLinesAndLegsAsync(string connectionString)
    {
        await using var context = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connectionString, sql => sql.CommandTimeout(3)).Options);
        var result = (Lines: 0, Legs: 0m);
        await context.ExecuteInTransactionAsync(System.Data.IsolationLevel.Snapshot, async token =>
        {
            result.Lines = await context.BankStatementLines.IgnoreQueryFilters().CountAsync(token);
            result.Legs = await context.TransactionCashLegs.IgnoreQueryFilters()
                .Where(leg => leg.Currency == "USD" && leg.Amount == 500m)
                .SumAsync(leg => (decimal?)leg.Amount, token) ?? 0m;
        }, CancellationToken.None);
        return result;
    }

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
