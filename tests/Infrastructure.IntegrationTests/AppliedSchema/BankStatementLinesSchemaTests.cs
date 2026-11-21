using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Schema;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

/// <summary>Deposit matching §6.2: <c>BankFeedCursors</c>, <c>BankStatementLines</c>, source kind 3 (script 0041).</summary>
public class BankStatementLinesSchemaTests
{
    private const string Script = "0041_bank_statement_lines.sql";

    [Test]
    public void SchemaDirectory_Includes0041Script()
    {
        new FileSchemaScriptSource(ScratchSchemaDatabase.SchemaDirectory()).GetScripts()
            .Select(script => script.Name)
            .ShouldContain(Script);
    }

    [Test]
    public async Task SameConnectionAndOutsideId_Twice_ViolatesTheUniqueIndex_EvenFromAnotherClientMoneyAccount()
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertLine("@cma", "simulator", "w-L1")}
            {InsertLine("@cma2", "simulator", "w-L1")}
            """));

        exception.Number.ShouldBe(2601);
        exception.Message.ShouldContain("UX_BankStatementLines_Connection_OutsideId");
    }

    [Test]
    public async Task SameOutsideId_OnAnotherConnection_Inserts()
    {
        var count = await RunAsync($"""
            {World()}
            {InsertLine("@cma", "simulator", "w-L1")}
            {InsertLine("@cma2", "other-bank", "w-L1")}
            SELECT COUNT(*) FROM [BankStatementLines] WHERE [OutsideId] = N'w-L1';
            """);

        count.ShouldBe(2);
    }

    [Test]
    public async Task Matched_WithoutTheMatchColumns_ViolatesTheMatchCheck()
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertLine("@cma", "simulator", "w-L1", status: 1)}
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain("CK_BankStatementLines_Match");
    }

    [TestCase(2)]
    [TestCase(0)]
    public async Task NotMatched_WithTheMatchColumns_ViolatesTheMatchCheck(int status)
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertLine("@cma", "simulator", "w-L1", status: status, matched: true)}
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain("CK_BankStatementLines_Match");
    }

    [Test]
    public async Task Matched_WithEveryMatchColumn_Inserts()
    {
        var status = await RunAsync($"""
            {World()}
            {InsertLine("@cma", "simulator", "w-L1", status: 1, matched: true)}
            SELECT [Status] FROM [BankStatementLines] WHERE [OutsideId] = N'w-L1';
            """);

        status.ShouldBe(1);
    }

    [TestCase("[Amount]", "0")]
    [TestCase("[Status]", "5")]
    [TestCase("[Direction]", "2")]
    [TestCase("[UnmatchedReason]", "8")]
    public async Task ValueOutsideItsCheck_IsRejected(string column, string value)
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertLine("@cma", "simulator", "w-L1")}
            UPDATE [BankStatementLines] SET {column} = {value} WHERE [OutsideId] = N'w-L1';
            """));

        exception.Number.ShouldBe(547);
    }

    [Test]
    public async Task Transaction_SourceKind3_NeedsASourcePublicId_AndKind5IsRejected()
    {
        (await RunAsync($"""
            {World()}
            {InsertTransaction(3, "NEWID()")}
            SELECT [SourceKind] FROM [Transactions] WHERE [Id] = SCOPE_IDENTITY();
            """)).ShouldBe(3);

        var withoutId = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertTransaction(3, "NULL")}
            """));
        withoutId.Number.ShouldBe(547);
        withoutId.Message.ShouldContain("CK_Transactions_SourcePublicId");

        // Withdrawals widened the check to Payment (4); 5 is still outside it.
        var kind5 = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertTransaction(5, "NEWID()")}
            """));
        kind5.Number.ShouldBe(547);
        kind5.Message.ShouldContain("CK_Transactions_SourceKind");
    }

    [Test]
    public async Task Cursor_NeedsAClientMoneyAccount()
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync("""
            INSERT INTO [BankFeedCursors] ([ClientMoneyAccountId]) VALUES (-1);
            """));

        exception.Number.ShouldBe(547);
    }

    [Test]
    public async Task EfRoundTrip_KeepsTheLineAndTheCursor()
    {
        await using var db = await ScratchSchemaDatabase.CreateAsync("BankStatementLinesEf");
        await db.ApplyAllAsync();
        (await db.ReadAsync("SELECT OBJECT_ID(N'BankStatementLines')")).Single()[0].ShouldNotBe(DBNull.Value);
        var usd = Currency.Create("USD", "US Dollar", 2);
        var tenant = Tenant.Create("Round Trip", Guid.NewGuid().ToString("N")[..12], Currency.Create("NZD", "New Zealand Dollar", 2));
        await using (var context = OpenDb(db.ConnectionString))
        {
            context.Tenants.Add(tenant);
            await context.SaveChangesAsync();
        }

        var account = ClientMoneyAccount.Create(tenant.Id, usd, "Simulator Bank", "Trust Account", "SIM-RT-USD", "simulator");
        account.CreatedBy = "test";
        await using (var context = OpenDb(db.ConnectionString))
        {
            context.ClientMoneyAccounts.Add(account);
            await context.SaveChangesAsync();
        }

        var receivedAt = new DateTimeOffset(2026, 11, 20, 2, 1, 7, TimeSpan.Zero);
        var line = BankStatementLine.Receive(
            tenant.Id, account.Id, "simulator", "w-L7", new DateOnly(2026, 11, 20), BankLineDirection.Credit, 500.1234m,
            "USD", "J SMITH", "****4200", "K7M3Q9PXB", receivedAt);
        line.MarkUnmatched(UnmatchedReason.UnknownReference);
        line.CreatedBy = "test";
        var cursor = BankFeedCursor.Start(account.Id);
        cursor.Advance("w:12", receivedAt);
        cursor.Fail(BankFeedFailure.InvalidLine, receivedAt.AddMinutes(1));
        await using (var context = OpenDb(db.ConnectionString))
        {
            context.BankStatementLines.Add(line);
            context.BankFeedCursors.Add(cursor);
            await context.SaveChangesAsync();
        }

        await using (var context = OpenDb(db.ConnectionString))
        {
            var reloaded = await context.BankStatementLines.AsNoTracking().SingleAsync(row => row.PublicId == line.PublicId);
            reloaded.TenantId.ShouldBe(tenant.Id);
            reloaded.ClientMoneyAccountId.ShouldBe(account.Id);
            reloaded.Connection.ShouldBe("simulator");
            reloaded.OutsideId.ShouldBe("w-L7");
            reloaded.ValueDate.ShouldBe(new DateOnly(2026, 11, 20));
            reloaded.Direction.ShouldBe(BankLineDirection.Credit);
            reloaded.Amount.ShouldBe(500.1234m);
            reloaded.Currency.ShouldBe("USD");
            reloaded.PayerName.ShouldBe("J SMITH");
            reloaded.PayerAccountMasked.ShouldBe("****4200");
            reloaded.ReferenceText.ShouldBe("K7M3Q9PXB");
            reloaded.ReceivedAt.ShouldBe(receivedAt);
            reloaded.Status.ShouldBe(BankStatementLineStatus.Unmatched);
            reloaded.UnmatchedReason.ShouldBe(UnmatchedReason.UnknownReference);
            reloaded.RowVersion.ShouldNotBeEmpty();

            var reloadedCursor = await context.BankFeedCursors.AsNoTracking()
                .SingleAsync(row => row.ClientMoneyAccountId == account.Id);
            reloadedCursor.Cursor.ShouldBe("w:12");
            reloadedCursor.LastSucceededAt.ShouldBe(receivedAt);
            reloadedCursor.LastFailedAt.ShouldBe(receivedAt.AddMinutes(1));
            reloadedCursor.LastFailure.ShouldBe(BankFeedFailure.InvalidLine);
            reloadedCursor.RowVersion.ShouldNotBeEmpty();
        }
    }

    /// <summary>A tenant (<c>@a_*</c> from <see cref="ScratchSchemaDatabase.TenantRows"/>) with two client money
    /// accounts, <c>@cma</c> and <c>@cma2</c>.</summary>
    private static string World()
    {
        var code = Guid.NewGuid().ToString("N")[..12];
        return $"""
            {ScratchSchemaDatabase.TenantRows("a", code)}
            INSERT INTO [ClientMoneyAccounts]
                ([PublicId], [TenantId], [Currency], [BankName], [AccountName], [AccountNumber], [IsActive], [Created], [CreatedBy], [BankConnection])
            VALUES (NEWID(), @a_tenant, 'USD', N'Simulator Bank', N'Trust', N'N-{code}-1', 1, SYSDATETIMEOFFSET(), N'test', N'simulator');
            DECLARE @cma int = SCOPE_IDENTITY();
            INSERT INTO [ClientMoneyAccounts]
                ([PublicId], [TenantId], [Currency], [BankName], [AccountName], [AccountNumber], [IsActive], [Created], [CreatedBy], [BankConnection])
            VALUES (NEWID(), @a_tenant, 'NZD', N'Simulator Bank', N'Trust', N'N-{code}-2', 1, SYSDATETIMEOFFSET(), N'test', N'simulator');
            DECLARE @cma2 int = SCOPE_IDENTITY();
            """;
    }

    private static string InsertLine(string clientMoneyAccount, string connection, string outsideId, int status = 0, bool matched = false) => $"""
        INSERT INTO [BankStatementLines]
            ([PublicId], [TenantId], [ClientMoneyAccountId], [Connection], [OutsideId], [ValueDate], [Direction], [Amount], [Currency],
             [ReceivedAt], [Status], [MatchedAccountId], [MatchedTransactionId], [MatchMethod], [MatchedAt], [Created], [CreatedBy])
        VALUES (NEWID(), @a_tenant, {clientMoneyAccount}, N'{connection}', N'{outsideId}', '2026-11-20', 0, 500, 'USD',
                SYSDATETIMEOFFSET(), {status},
                {(matched ? "@a_bank" : "NULL")}, {(matched ? "@a_buy" : "NULL")}, {(matched ? "0" : "NULL")}, {(matched ? "SYSDATETIMEOFFSET()" : "NULL")},
                SYSDATETIMEOFFSET(), N'test');
        """;

    private static string InsertTransaction(int kind, string sourcePublicId) => $"""
        INSERT INTO [Transactions] ([PublicId], [TenantId], [Type], [SourceKind], [SourcePublicId], [BookedAt], [Created], [CreatedBy])
        VALUES (NEWID(), @a_tenant, 0, {kind}, {sourcePublicId}, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'test');
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
