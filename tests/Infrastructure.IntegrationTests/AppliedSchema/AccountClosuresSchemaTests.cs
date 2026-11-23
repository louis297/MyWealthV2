using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Schema;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

/// <summary>Account closure §6: the <c>AccountClosures</c> table and the widened source kind and unmatched reason
/// checks (script 0046).</summary>
public class AccountClosuresSchemaTests
{
    private const string Script = "0046_account_closures.sql";

    [Test]
    public void SchemaDirectory_Includes0046Script()
    {
        new FileSchemaScriptSource(ScratchSchemaDatabase.SchemaDirectory()).GetScripts()
            .Select(script => script.Name)
            .ShouldContain(Script);
    }

    [Test]
    public async Task AClosureInProgress_WithItsWithdrawal_Inserts()
    {
        (await RunAsync($"""
            {World()}
            {InsertWithdrawal()}
            DECLARE @w int = SCOPE_IDENTITY();
            {InsertClosure(0, "NULL", 300.004m, 300m, "@nba", "@w", "NULL", "NULL", "NULL", "NULL")}
            SELECT [Status] FROM [AccountClosures] WHERE [Id] = SCOPE_IDENTITY();
            """)).ShouldBe(0);
    }

    [Test]
    public async Task TwoClosuresInProgressOfOneAccount_ViolateTheFilteredIndex()
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertWithdrawal()}
            DECLARE @w int = SCOPE_IDENTITY();
            {InsertWithdrawal()}
            DECLARE @w2 int = SCOPE_IDENTITY();
            {InsertClosure(0, "NULL", 300m, 300m, "@nba", "@w", "NULL", "NULL", "NULL", "NULL")}
            {InsertClosure(0, "NULL", 300m, 300m, "@nba", "@w2", "NULL", "NULL", "NULL", "NULL")}
            """));

        exception.Number.ShouldBe(2601);
        exception.Message.ShouldContain("UX_AccountClosures_InProgress");
    }

    [Test]
    public async Task TwoCompletedClosuresOfOneAccount_AreAllowed()
    {
        (await RunAsync($"""
            {World()}
            {InsertClosure(1, "NULL", 0m, 0m, "NULL", "NULL", "NULL", "NULL", "SYSDATETIMEOFFSET()", "NULL")}
            {InsertClosure(1, "NULL", 0m, 0m, "NULL", "NULL", "NULL", "NULL", "SYSDATETIMEOFFSET()", "NULL")}
            SELECT COUNT(*) FROM [AccountClosures] WHERE [AccountId] = @a_bank;
            """)).ShouldBe(2);
    }

    [Test]
    public async Task ASecondClosureWithTheSameWithdrawal_ViolatesTheWithdrawalIndex()
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertWithdrawal()}
            DECLARE @w int = SCOPE_IDENTITY();
            {InsertClosure(2, "0", 300m, 300m, "@nba", "@w", "NULL", "NULL", "NULL", "SYSDATETIMEOFFSET()")}
            {InsertClosure(0, "NULL", 300m, 300m, "@nba", "@w", "NULL", "NULL", "NULL", "NULL")}
            """));

        exception.Number.ShouldBe(2601);
        exception.Message.ShouldContain("UX_AccountClosures_Withdrawal");
    }

    [TestCase("[Status]", "3", "CK_AccountClosures_Status")]
    [TestCase("[CashBalanceAtRequest]", "-1", "CK_AccountClosures_CashBalanceAtRequest")]
    [TestCase("[PayoutAmount]", "-1", "CK_AccountClosures_PayoutAmount")]
    public async Task AValueOutsideItsCheck_IsRejected(string column, string value, string check)
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertClosure(1, "NULL", 0m, 0m, "NULL", "NULL", "NULL", "NULL", "SYSDATETIMEOFFSET()", "NULL")}
            UPDATE [AccountClosures] SET {column} = {value} WHERE [Id] = SCOPE_IDENTITY();
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain(check);
    }

    [TestCase(2, "4", "SYSDATETIMEOFFSET()", "CK_AccountClosures_CancelReason")]
    [TestCase(2, "NULL", "SYSDATETIMEOFFSET()", "CK_AccountClosures_Cancel")]
    [TestCase(1, "0", "NULL", "CK_AccountClosures_Cancel")]
    public async Task ACancelReasonNotSetExactlyWhenCancelled_IsRejected(int status, string reason, string endedAt, string check)
    {
        var completedAt = status == 1 ? "SYSDATETIMEOFFSET()" : "NULL";
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertClosure(status, reason, 0m, 0m, "NULL", "NULL", "NULL", "NULL", completedAt, endedAt)}
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain(check);
    }

    [TestCase(1, "NULL", "NULL", "CK_AccountClosures_Completed")]
    [TestCase(0, "SYSDATETIMEOFFSET()", "NULL", "CK_AccountClosures_Completed")]
    [TestCase(0, "NULL", "SYSDATETIMEOFFSET()", "CK_AccountClosures_Ended")]
    public async Task ACompletedOrEndedTimeNotSetExactlyForItsStatus_IsRejected(
        int status, string completedAt, string endedAt, string check)
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertWithdrawal()}
            DECLARE @w int = SCOPE_IDENTITY();
            {InsertClosure(status, "NULL", 300m, 300m, "@nba", "@w", "NULL", "NULL", completedAt, endedAt)}
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain(check);
    }

    [TestCase("300", "@nba", "NULL")]
    [TestCase("300", "NULL", "@w")]
    [TestCase("0", "@nba", "@w")]
    public async Task APayoutWithoutBothItsBankAccountAndWithdrawal_IsRejected(string payout, string bank, string withdrawal)
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertWithdrawal()}
            DECLARE @w int = SCOPE_IDENTITY();
            {InsertClosure(2, "0", 300m, decimal.Parse(payout), bank, withdrawal, "NULL", "NULL", "NULL", "SYSDATETIMEOFFSET()")}
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain("CK_AccountClosures_Payout");
    }

    [TestCase("0.004", "NULL", "CK_AccountClosures_WriteOff")]
    [TestCase("NULL", "@a_buy", "CK_AccountClosures_WriteOff")]
    [TestCase("0", "@a_buy", "CK_AccountClosures_WriteOffAmount")]
    public async Task AWriteOffWithoutBothItsAmountAndTransaction_IsRejected(string amount, string transaction, string check)
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertClosure(1, "NULL", 0.004m, 0m, "NULL", "NULL", amount, transaction, "SYSDATETIMEOFFSET()", "NULL")}
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain(check);
    }

    [Test]
    public async Task ACompletedClosureWithAWriteOff_Inserts()
    {
        (await RunAsync($"""
            {World()}
            {InsertClosure(1, "NULL", 0.004m, 0m, "NULL", "NULL", "0.004", "@a_buy", "SYSDATETIMEOFFSET()", "NULL")}
            SELECT [Status] FROM [AccountClosures] WHERE [Id] = SCOPE_IDENTITY();
            """)).ShouldBe(1);
    }

    [Test]
    public async Task Transaction_SourceKind5_NeedsASourcePublicId_AndKind10IsRejected()
    {
        (await RunAsync($"""
            {World()}
            {InsertTransaction(5, "NEWID()")}
            SELECT [SourceKind] FROM [Transactions] WHERE [Id] = SCOPE_IDENTITY();
            """)).ShouldBe(5);

        var withoutId = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertTransaction(5, "NULL")}
            """));
        withoutId.Number.ShouldBe(547);
        withoutId.Message.ShouldContain("CK_Transactions_SourcePublicId");

        var kind9 = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertTransaction(10, "NEWID()")}
            """));
        kind9.Number.ShouldBe(547);
        kind9.Message.ShouldContain("CK_Transactions_SourceKind");
    }

    [Test]
    public async Task StatementLine_TakesUnmatchedReason12_And13IsRejected()
    {
        (await RunAsync($"""
            {World()}
            {InsertLine("w-L1")}
            UPDATE [BankStatementLines] SET [Status] = 2, [UnmatchedReason] = 12 WHERE [OutsideId] = N'w-L1';
            SELECT [UnmatchedReason] FROM [BankStatementLines] WHERE [OutsideId] = N'w-L1' AND [TenantId] = @a_tenant;
            """)).ShouldBe(12);

        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertLine("w-L1")}
            UPDATE [BankStatementLines] SET [Status] = 2, [UnmatchedReason] = 14 WHERE [OutsideId] = N'w-L1';
            """));
        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain("CK_BankStatementLines_UnmatchedReason");
    }

    [Test]
    public async Task EfRoundTrip_KeepsAClosureThroughItsCompletion()
    {
        await using var db = await ScratchSchemaDatabase.CreateAsync("AccountClosuresEf");
        await db.ApplyAllAsync();
        var ids = (await db.ReadAsync($"""
            {World()}
            {InsertWithdrawal()}
            SELECT @a_tenant, @a_bank, @a_adviser, @nba, SCOPE_IDENTITY(), @a_buy;
            """)).Single().Select(Convert.ToInt32).ToArray();
        var (tenantId, accountId, adviserId, bankId, withdrawalId, transactionId) =
            (ids[0], ids[1], ids[2], ids[3], ids[4], ids[5]);
        var at = new DateTimeOffset(2026, 11, 22, 3, 0, 0, TimeSpan.Zero);
        var closure = AccountClosure.StartWithPayout(
            tenantId, accountId, "USD", adviserId, at, "Moving", 300.004m, 300.00m, bankId, withdrawalId);
        closure.CreatedBy = "test";
        await using (var context = OpenDb(db.ConnectionString))
        {
            context.AccountClosures.Add(closure);
            await context.SaveChangesAsync();
        }

        await using (var context = OpenDb(db.ConnectionString))
        {
            var stored = await context.AccountClosures.SingleAsync(row => row.Id == closure.Id);
            stored.Status.ShouldBe(AccountClosureStatus.InProgress);
            stored.RowVersion.ShouldNotBeEmpty();
            stored.Complete(0.004m, transactionId, at.AddMinutes(12));
            await context.SaveChangesAsync();
        }

        await using (var context = OpenDb(db.ConnectionString))
        {
            var stored = await context.AccountClosures.AsNoTracking().SingleAsync(row => row.Id == closure.Id);
            stored.PublicId.ShouldBe(closure.PublicId);
            stored.TenantId.ShouldBe(tenantId);
            stored.AccountId.ShouldBe(accountId);
            stored.Status.ShouldBe(AccountClosureStatus.Completed);
            stored.CancelReason.ShouldBeNull();
            stored.Memo.ShouldBe("Moving");
            stored.RequestedByUserId.ShouldBe(adviserId);
            stored.RequestedAt.ShouldBe(at);
            stored.Currency.ShouldBe("USD");
            stored.CashBalanceAtRequest.ShouldBe(300.004m);
            stored.PayoutAmount.ShouldBe(300.00m);
            stored.NominatedBankAccountId.ShouldBe(bankId);
            stored.WithdrawalId.ShouldBe(withdrawalId);
            stored.WriteOffAmount.ShouldBe(0.004m);
            stored.WriteOffTransactionId.ShouldBe(transactionId);
            stored.CompletedAt.ShouldBe(at.AddMinutes(12));
            stored.EndedAt.ShouldBeNull();
        }
    }

    /// <summary>A tenant (<c>@a_*</c> from <see cref="ScratchSchemaDatabase.TenantRows"/>) with a USD client money
    /// account <c>@cma</c> and a nominated bank account <c>@nba</c> of <c>@a_customer</c>. The closures are
    /// of the USD account <c>@a_bank</c>.</summary>
    private static string World()
    {
        var code = Guid.NewGuid().ToString("N")[..12];
        return $"""
            {ScratchSchemaDatabase.TenantRows("a", code)}
            INSERT INTO [ClientMoneyAccounts]
                ([PublicId], [TenantId], [Currency], [BankName], [AccountName], [AccountNumber], [IsActive], [Created], [CreatedBy], [LastModified], [BankConnection])
            VALUES (NEWID(), @a_tenant, 'USD', N'Simulator Bank', N'Trust', N'N-{code}', 1, SYSDATETIMEOFFSET(), N'test', SYSDATETIMEOFFSET(), N'simulator');
            DECLARE @cma int = SCOPE_IDENTITY();
            INSERT INTO [NominatedBankAccounts]
                ([PublicId], [TenantId], [CustomerId], [Currency], [BankName], [AccountName], [AccountNumber], [AccountNumberKey], [Status],
                 [AddedByUserId], [AddedAt], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @a_customer, 'USD', N'ANZ', N'J Smith', N'0601', N'0601', 0, @a_adviser, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'test');
            DECLARE @nba int = SCOPE_IDENTITY();
            """;
    }

    private static string InsertWithdrawal() => """
        INSERT INTO [Withdrawals]
            ([PublicId], [TenantId], [AccountId], [NominatedBankAccountId], [Amount], [Currency], [Reference], [Status],
             [RequestedByUserId], [RequestedAt], [Created], [CreatedBy])
        VALUES (NEWID(), @a_tenant, @a_bank, @nba, 300, 'USD', N'K7M3Q9PXB', 0, @a_adviser, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'test');
        """;

    private static string InsertClosure(
        int status,
        string cancelReason,
        decimal cash,
        decimal payout,
        string bank,
        string withdrawal,
        string writeOffAmount,
        string writeOffTransaction,
        string completedAt,
        string endedAt) => $"""
        INSERT INTO [AccountClosures]
            ([PublicId], [TenantId], [AccountId], [Status], [CancelReason], [Memo], [RequestedByUserId], [RequestedAt], [Currency],
             [CashBalanceAtRequest], [PayoutAmount], [NominatedBankAccountId], [WithdrawalId], [WriteOffAmount], [WriteOffTransactionId],
             [CompletedAt], [EndedAt], [Created], [CreatedBy])
        VALUES (NEWID(), @a_tenant, @a_bank, {status}, {cancelReason}, N'Moving', @a_adviser, SYSDATETIMEOFFSET(), 'USD',
                {cash.ToString(System.Globalization.CultureInfo.InvariantCulture)}, {payout.ToString(System.Globalization.CultureInfo.InvariantCulture)},
                {bank}, {withdrawal}, {writeOffAmount}, {writeOffTransaction}, {completedAt}, {endedAt}, SYSDATETIMEOFFSET(), N'test');
        """;

    private static string InsertTransaction(int sourceKind, string sourcePublicId) => $"""
        INSERT INTO [Transactions] ([PublicId], [TenantId], [Type], [SourceKind], [SourcePublicId], [BookedAt], [Created], [CreatedBy])
        VALUES (NEWID(), @a_tenant, 2, {sourceKind}, {sourcePublicId}, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'test');
        """;

    private static string InsertLine(string outsideId) => $"""
        INSERT INTO [BankStatementLines]
            ([PublicId], [TenantId], [ClientMoneyAccountId], [Connection], [OutsideId], [ValueDate], [Direction], [Amount], [Currency],
             [ReceivedAt], [Status], [Created], [CreatedBy])
        VALUES (NEWID(), @a_tenant, @cma, N'simulator', N'{outsideId}', '2026-11-22', 0, 300, 'USD', SYSDATETIMEOFFSET(), 0,
                SYSDATETIMEOFFSET(), N'test');
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
