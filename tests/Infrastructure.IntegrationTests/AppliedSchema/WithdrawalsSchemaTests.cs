using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Schema;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

/// <summary>Withdrawals §6.2: <c>Withdrawals</c>, <c>OutgoingPayments</c>, <c>TransactionPaymentDetails</c>, the
/// payout columns of <c>BankStatementLines</c> and source kind 4 (script 0043).</summary>
public class WithdrawalsSchemaTests
{
    private const string Script = "0043_withdrawals.sql";

    [Test]
    public void SchemaDirectory_Includes0043Script()
    {
        new FileSchemaScriptSource(ScratchSchemaDatabase.SchemaDirectory()).GetScripts()
            .Select(script => script.Name)
            .ShouldContain(Script);
    }

    [TestCase(0, null, null, null)]
    [TestCase(1, null, null, null)]
    [TestCase(2, null, null, null)]
    [TestCase(3, "@a_buy", null, null)]
    [TestCase(4, null, null, null)]
    [TestCase(5, null, null, null)]
    [TestCase(6, null, null, "3")]
    [TestCase(7, "@a_buy", "@a_fx", null)]
    public async Task EachStatus_WithItsColumns_Inserts(int status, string? payout, string? returned, string? failure)
    {
        (await RunAsync($"""
            {World()}
            {InsertWithdrawal(status, payout, returned, failure)}
            SELECT [Status] FROM [Withdrawals] WHERE [Id] = SCOPE_IDENTITY();
            """)).ShouldBe(status);
    }

    [TestCase(1, "@a_buy", null, null, "CK_Withdrawals_Payout")]
    [TestCase(3, null, null, null, "CK_Withdrawals_Payout")]
    [TestCase(7, null, "@a_fx", null, "CK_Withdrawals_Payout")]
    [TestCase(3, "@a_buy", "@a_fx", null, "CK_Withdrawals_Return")]
    [TestCase(7, "@a_buy", null, null, "CK_Withdrawals_Return")]
    [TestCase(6, null, null, null, "CK_Withdrawals_Failure")]
    [TestCase(4, null, null, "0", "CK_Withdrawals_Failure")]
    [TestCase(8, null, null, null, "CK_Withdrawals_Status")]
    public async Task ColumnsThatDoNotMatchTheStatus_ViolateTheirCheck(int status, string? payout, string? returned, string? failure, string check)
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertWithdrawal(status, payout, returned, failure)}
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain(check);
    }

    [TestCase("[Amount]", "0", "CK_Withdrawals_Amount")]
    [TestCase("[DecisionAction]", "3", "CK_Withdrawals_DecisionAction")]
    [TestCase("[DecisionByUserId]", "NULL", "CK_Withdrawals_Decision")]
    public async Task WithdrawalValueOutsideItsCheck_IsRejected(string column, string value, string check)
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertWithdrawal(1, null, null, null)}
            UPDATE [Withdrawals] SET {column} = {value} WHERE [Id] = SCOPE_IDENTITY();
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain(check);
    }

    [Test]
    public async Task ADecisionWithoutANote_Inserts()
    {
        (await RunAsync($"""
            {World()}
            {InsertWithdrawal(1, null, null, null)}
            UPDATE [Withdrawals] SET [DecisionNote] = NULL WHERE [Id] = SCOPE_IDENTITY();
            SELECT COUNT(*) FROM [Withdrawals] WHERE [TenantId] = @a_tenant AND [DecisionNote] IS NULL;
            """)).ShouldBe(1);
    }

    [Test]
    public async Task TwoPayments_WithOneInstructionId_ViolateTheUniqueIndex()
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertWithdrawal(1, null, null, null)}
            DECLARE @w int = SCOPE_IDENTITY();
            DECLARE @instruction uniqueidentifier = NEWID();
            {InsertPayment("@w", "@instruction")}
            {InsertPayment("@w", "@instruction")}
            """));

        exception.Number.ShouldBe(2601);
        exception.Message.ShouldContain("UX_OutgoingPayments_Instruction");
    }

    [TestCase("[Status]", "6")]
    [TestCase("[LastFailure]", "6")]
    [TestCase("[RejectReason]", "4")]
    [TestCase("[Attempts]", "-1")]
    [TestCase("[Amount]", "0")]
    public async Task PaymentValueOutsideItsCheck_IsRejected(string column, string value)
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertWithdrawal(1, null, null, null)}
            DECLARE @w int = SCOPE_IDENTITY();
            {InsertPayment("@w", "NEWID()")}
            UPDATE [OutgoingPayments] SET {column} = {value} WHERE [Id] = SCOPE_IDENTITY();
            """));

        exception.Number.ShouldBe(547);
    }

    [Test]
    public async Task Transaction_SourceKind4_NeedsASourcePublicId_AndKind5IsRejected()
    {
        (await RunAsync($"""
            {World()}
            {InsertTransaction(4, "NEWID()")}
            SELECT [SourceKind] FROM [Transactions] WHERE [Id] = SCOPE_IDENTITY();
            """)).ShouldBe(4);

        var withoutId = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertTransaction(4, "NULL")}
            """));
        withoutId.Number.ShouldBe(547);
        withoutId.Message.ShouldContain("CK_Transactions_SourcePublicId");

        var kind5 = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertTransaction(5, "NEWID()")}
            """));
        kind5.Number.ShouldBe(547);
        kind5.Message.ShouldContain("CK_Transactions_SourceKind");
    }

    [Test]
    public async Task StatementLine_TakesThePayoutColumns_Reason7_AndMethod3()
    {
        (await RunAsync($"""
            {World()}
            {InsertWithdrawal(3, "@a_buy", null, null)}
            DECLARE @w int = SCOPE_IDENTITY();
            {InsertLine("w-L1", "NULL")}
            DECLARE @payout int = SCOPE_IDENTITY();
            {InsertLine("w-L2", "@payout")}
            UPDATE [BankStatementLines]
               SET [EndToEndId] = CONVERT(nvarchar(100), NEWID()), [WithdrawalId] = @w, [Status] = 1, [MatchedAccountId] = @a_bank,
                   [MatchedTransactionId] = @a_buy, [MatchMethod] = 3, [MatchedAt] = SYSDATETIMEOFFSET()
             WHERE [Id] = @payout;
            UPDATE [BankStatementLines] SET [Status] = 2, [UnmatchedReason] = 7, [ReturnReason] = N'Account closed'
             WHERE [OutsideId] = N'w-L2';
            SELECT COUNT(*) FROM [BankStatementLines] WHERE [TenantId] = @a_tenant AND ([MatchMethod] = 3 OR [UnmatchedReason] = 7);
            """)).ShouldBe(2);
    }

    [TestCase("[UnmatchedReason]", "8")]
    [TestCase("[MatchMethod]", "4")]
    public async Task StatementLineValueOutsideItsWidenedCheck_IsRejected(string column, string value)
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertLine("w-L1", "NULL")}
            UPDATE [BankStatementLines] SET {column} = {value} WHERE [OutsideId] = N'w-L1';
            """));

        exception.Number.ShouldBe(547);
    }

    [Test]
    public async Task PaymentDetail_NeedsATransactionOfItsTenant()
    {
        (await RunAsync($"""
            {World()}
            INSERT INTO [TransactionPaymentDetails] ([TransactionId], [TenantId], [ValueDate], [Reference], [PayeeName], [PayeeAccount])
            VALUES (@a_buy, @a_tenant, '2026-11-20', N'K7M3Q9PXB', N'J Smith', N'****8900');
            SELECT COUNT(*) FROM [TransactionPaymentDetails] WHERE [TransactionId] = @a_buy;
            """)).ShouldBe(1);

        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync("""
            INSERT INTO [TransactionPaymentDetails] ([TransactionId], [TenantId], [ValueDate]) VALUES (-1, -1, '2026-11-20');
            """));
        exception.Number.ShouldBe(547);
    }

    [Test]
    public async Task EfRoundTrip_KeepsTheWithdrawalAndItsPayment()
    {
        await using var db = await ScratchSchemaDatabase.CreateAsync("WithdrawalsEf");
        await db.ApplyAllAsync();
        var code = Guid.NewGuid().ToString("N")[..12];
        var ids = (await db.ReadAsync($"""
            {ScratchSchemaDatabase.TenantRows("a", code)}
            INSERT INTO [ClientMoneyAccounts]
                ([PublicId], [TenantId], [Currency], [BankName], [AccountName], [AccountNumber], [IsActive], [Created], [CreatedBy], [LastModified], [BankConnection])
            VALUES (NEWID(), @a_tenant, 'USD', N'Simulator Bank', N'Trust', N'N-{code}', 1, SYSDATETIMEOFFSET(), N'test', SYSDATETIMEOFFSET(), N'simulator');
            DECLARE @cma int = SCOPE_IDENTITY();
            {InsertNominated()}
            SELECT @a_tenant, @a_bank, SCOPE_IDENTITY(), @a_adviser, @a_customer, @cma, @a_buy;
            """)).Single().Select(Convert.ToInt32).ToArray();
        var (tenantId, accountId, nominatedId, adviser, admin, clientMoneyId, payoutId) = (ids[0], ids[1], ids[2], ids[3], ids[4], ids[5], ids[6]);
        var at = new DateTimeOffset(2026, 11, 20, 4, 0, 0, TimeSpan.Zero);

        var withdrawal = Withdrawal.Request(tenantId, accountId, nominatedId, 300.25m, "USD", "K7M3Q9PXB", "By phone", adviser, at);
        withdrawal.Approve(admin, "Fine", at.AddMinutes(5));
        withdrawal.CreatedBy = "test";
        await using (var context = OpenDb(db.ConnectionString))
        {
            context.Withdrawals.Add(withdrawal);
            await context.SaveChangesAsync();
        }

        await using (var context = OpenDb(db.ConnectionString))
        {
            var clientMoney = await context.ClientMoneyAccounts.SingleAsync(row => row.Id == clientMoneyId);
            var payment = OutgoingPayment.ForWithdrawal(withdrawal, clientMoney);
            payment.RecordAttempt(at.AddMinutes(6));
            payment.Accept("w1-P4", at.AddMinutes(6).AddSeconds(1));
            payment.CreatedBy = "test";
            context.OutgoingPayments.Add(payment);
            var tracked = await context.Withdrawals.SingleAsync(row => row.Id == withdrawal.Id);
            tracked.MarkSent();
            tracked.MarkPaid(payoutId);
            await context.SaveChangesAsync();
        }

        await using (var context = OpenDb(db.ConnectionString))
        {
            var reloaded = await context.Withdrawals.AsNoTracking().SingleAsync(row => row.PublicId == withdrawal.PublicId);
            reloaded.TenantId.ShouldBe(tenantId);
            reloaded.AccountId.ShouldBe(accountId);
            reloaded.NominatedBankAccountId.ShouldBe(nominatedId);
            reloaded.Amount.ShouldBe(300.25m);
            reloaded.Currency.ShouldBe("USD");
            reloaded.Reference.ShouldBe("K7M3Q9PXB");
            reloaded.Memo.ShouldBe("By phone");
            reloaded.Status.ShouldBe(WithdrawalStatus.Paid);
            reloaded.RequestedByUserId.ShouldBe(adviser);
            reloaded.RequestedAt.ShouldBe(at);
            reloaded.DecisionAction.ShouldBe(WithdrawalDecision.Approved);
            reloaded.DecisionByUserId.ShouldBe(admin);
            reloaded.DecisionNote.ShouldBe("Fine");
            reloaded.PayoutTransactionId.ShouldBe(payoutId);
            reloaded.RowVersion.ShouldNotBeEmpty();

            var payment = await context.OutgoingPayments.AsNoTracking().SingleAsync(row => row.WithdrawalId == withdrawal.Id);
            payment.InstructionPublicId.ShouldBe(withdrawal.PublicId);
            payment.ClientMoneyAccountId.ShouldBe(clientMoneyId);
            payment.Connection.ShouldBe("simulator");
            payment.Amount.ShouldBe(300.25m);
            payment.Reference.ShouldBe("K7M3Q9PXB");
            payment.Status.ShouldBe(OutgoingPaymentStatus.Accepted);
            payment.Attempts.ShouldBe(1);
            payment.LastAttemptAt.ShouldBe(at.AddMinutes(6));
            payment.BankPaymentId.ShouldBe("w1-P4");
            payment.AcceptedAt.ShouldBe(at.AddMinutes(6).AddSeconds(1));
            payment.RowVersion.ShouldNotBeEmpty();
        }
    }

    /// <summary>A tenant (<c>@a_*</c> from <see cref="ScratchSchemaDatabase.TenantRows"/>) with a USD client money
    /// account <c>@cma</c> and a Verified nominated bank account <c>@nba</c> of <c>@a_customer</c>.</summary>
    private static string World()
    {
        var code = Guid.NewGuid().ToString("N")[..12];
        return $"""
            {ScratchSchemaDatabase.TenantRows("a", code)}
            INSERT INTO [ClientMoneyAccounts]
                ([PublicId], [TenantId], [Currency], [BankName], [AccountName], [AccountNumber], [IsActive], [Created], [CreatedBy], [BankConnection])
            VALUES (NEWID(), @a_tenant, 'USD', N'Simulator Bank', N'Trust', N'N-{code}', 1, SYSDATETIMEOFFSET(), N'test', N'simulator');
            DECLARE @cma int = SCOPE_IDENTITY();
            {InsertNominated()}
            DECLARE @nba int = SCOPE_IDENTITY();
            """;
    }

    private static string InsertNominated() => """
        INSERT INTO [NominatedBankAccounts]
            ([PublicId], [TenantId], [CustomerId], [Currency], [BankName], [AccountName], [AccountNumber], [AccountNumberKey], [Status],
             [AddedByUserId], [AddedAt], [ReviewedByUserId], [ReviewedAt], [ReviewNote], [Created], [CreatedBy])
        VALUES (NEWID(), @a_tenant, @a_customer, 'USD', N'ANZ', N'J Smith', N'0601', N'0601', 1,
                @a_adviser, SYSDATETIMEOFFSET(), @a_customer, SYSDATETIMEOFFSET(), N'Checked', SYSDATETIMEOFFSET(), N'test');
        """;

    private static string InsertWithdrawal(int status, string? payout, string? returned, string? failure)
    {
        var decided = status is not (0 or 8);
        return $"""
            INSERT INTO [Withdrawals]
                ([PublicId], [TenantId], [AccountId], [NominatedBankAccountId], [Amount], [Currency], [Reference], [Status],
                 [RequestedByUserId], [RequestedAt], [DecisionAction], [DecisionByUserId], [DecisionAt], [DecisionNote],
                 [PayoutTransactionId], [ReturnTransactionId], [FailureReason], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @a_bank, @nba, 300, 'USD', N'K7M3Q9PXB', {status},
                    @a_adviser, SYSDATETIMEOFFSET(),
                    {(decided ? "0, @a_customer, SYSDATETIMEOFFSET(), N'Fine'" : "NULL, NULL, NULL, NULL")},
                    {payout ?? "NULL"}, {returned ?? "NULL"}, {failure ?? "NULL"}, SYSDATETIMEOFFSET(), N'test');
            """;
    }

    private static string InsertPayment(string withdrawal, string instruction) => $"""
        INSERT INTO [OutgoingPayments]
            ([PublicId], [TenantId], [ClientMoneyAccountId], [Connection], [InstructionPublicId], [WithdrawalId], [Amount], [Currency],
             [Reference], [Status], [Created], [CreatedBy])
        VALUES (NEWID(), @a_tenant, @cma, N'simulator', {instruction}, {withdrawal}, 300, 'USD', N'K7M3Q9PXB', 0, SYSDATETIMEOFFSET(), N'test');
        """;

    private static string InsertLine(string outsideId, string returnOf) => $"""
        INSERT INTO [BankStatementLines]
            ([PublicId], [TenantId], [ClientMoneyAccountId], [Connection], [OutsideId], [ValueDate], [Direction], [Amount], [Currency],
             [ReceivedAt], [Status], [RelatedLineId], [Created], [CreatedBy])
        VALUES (NEWID(), @a_tenant, @cma, N'simulator', N'{outsideId}', '2026-11-20', 1, 300, 'USD', SYSDATETIMEOFFSET(), 0, {returnOf},
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
