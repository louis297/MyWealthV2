using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Schema;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

/// <summary>Bank-returns §6.1: the kind and the returned line of <c>OutgoingPayments</c>, and the widened checks and
/// the new Matched CHECK of <c>BankStatementLines</c> (script 0044).</summary>
public class BankReturnsSchemaTests
{
    private const string Script = "0044_bank_returns.sql";

    [Test]
    public void SchemaDirectory_Includes0044Script()
    {
        new FileSchemaScriptSource(ScratchSchemaDatabase.SchemaDirectory()).GetScripts()
            .Select(script => script.Name)
            .ShouldContain(Script);
    }

    [Test]
    public async Task Apply0044_GivesEveryExistingPaymentKind0_AndLeavesNoDefault()
    {
        await using var db = await ScratchSchemaDatabase.CreateAsync("BankReturns0044");
        await db.ApplyBeforeAsync(Script);
        await db.ExecuteAsync($"""
            {World()}
            {InsertWithdrawal()}
            DECLARE @w int = SCOPE_IDENTITY();
            INSERT INTO [OutgoingPayments]
                ([PublicId], [TenantId], [ClientMoneyAccountId], [Connection], [InstructionPublicId], [WithdrawalId], [Amount], [Currency],
                 [Reference], [Status], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @cma, N'simulator', NEWID(), @w, 300, 'USD', N'K7M3Q9PXB', 0, SYSDATETIMEOFFSET(), N'test');
            """);

        await db.ApplyAllAsync();

        var column = (await db.ReadAsync("""
            SELECT c.[is_nullable], c.[default_object_id]
            FROM sys.columns c
            WHERE c.[object_id] = OBJECT_ID(N'OutgoingPayments') AND c.[name] = N'Kind';
            """)).ShouldHaveSingleItem();
        ((bool)column[0]).ShouldBeFalse();
        ((int)column[1]).ShouldBe(0);
        var kinds = await db.ReadAsync("SELECT [Kind], [ReturnOfLineId] FROM [OutgoingPayments];");
        kinds.ShouldHaveSingleItem()[0].ShouldBe(0);
        kinds.Single()[1].ShouldBe(DBNull.Value);
        var withdrawalId = (await db.ReadAsync("""
            SELECT c.[is_nullable] FROM sys.columns c
            WHERE c.[object_id] = OBJECT_ID(N'OutgoingPayments') AND c.[name] = N'WithdrawalId';
            """)).Single()[0];
        ((bool)withdrawalId).ShouldBeTrue();
        var versions = await db.ReadAsync($"SELECT COUNT(*) FROM [SchemaVersions] WHERE [ScriptName] = N'{Script}';");
        ((int)versions.Single()[0]).ShouldBe(1);
    }

    [Test]
    public async Task AReceiptReturn_NamesTheLine_WithNoWithdrawal()
    {
        (await RunAsync($"""
            {World()}
            {InsertLine("w-L1")}
            DECLARE @line int = SCOPE_IDENTITY();
            {InsertPayment(1, "NULL", "@line", 0)}
            SELECT [Kind] FROM [OutgoingPayments] WHERE [Id] = SCOPE_IDENTITY();
            """)).ShouldBe(1);
    }

    [TestCase(0, false, true)]
    [TestCase(0, false, false)]
    [TestCase(0, true, true)]
    [TestCase(1, true, false)]
    [TestCase(1, false, false)]
    [TestCase(1, true, true)]
    public async Task APaymentWithoutExactlyTheTargetOfItsKind_ViolatesTheKindCheck(int kind, bool withdrawal, bool line)
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertWithdrawal()}
            DECLARE @w int = SCOPE_IDENTITY();
            {InsertLine("w-L1")}
            DECLARE @line int = SCOPE_IDENTITY();
            {InsertPayment(kind, withdrawal ? "@w" : "NULL", line ? "@line" : "NULL", 0)}
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain("CK_OutgoingPayments_Target");
    }

    [Test]
    public async Task AKindOutsideItsCheck_IsRejected()
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertLine("w-L1")}
            DECLARE @line int = SCOPE_IDENTITY();
            {InsertPayment(1, "NULL", "@line", 0)}
            UPDATE [OutgoingPayments] SET [Kind] = 2 WHERE [Id] = SCOPE_IDENTITY();
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain("CK_OutgoingPayments_Kind");
    }

    [TestCase(2)]
    [TestCase(3)]
    public async Task ALineWithAClosedReturn_TakesAnOpenOne(int closedStatus)
    {
        (await RunAsync($"""
            {World()}
            {InsertLine("w-L1")}
            DECLARE @line int = SCOPE_IDENTITY();
            {InsertPayment(1, "NULL", "@line", closedStatus)}
            {InsertPayment(1, "NULL", "@line", 0)}
            SELECT COUNT(*) FROM [OutgoingPayments] WHERE [ReturnOfLineId] = @line;
            """)).ShouldBe(2);
    }

    [TestCase(0, 0)]
    [TestCase(0, 1)]
    [TestCase(1, 1)]
    public async Task TwoOpenReturnsOfOneLine_ViolateTheFilteredIndex(int first, int second)
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertLine("w-L1")}
            DECLARE @line int = SCOPE_IDENTITY();
            {InsertPayment(1, "NULL", "@line", first)}
            {InsertPayment(1, "NULL", "@line", second)}
            """));

        exception.Number.ShouldBe(2601);
        exception.Message.ShouldContain("UX_OutgoingPayments_ReturnOfLineId_Open");
    }

    [Test]
    public async Task RejectReason4_IsAccepted_And5IsRejected()
    {
        (await RunAsync($"""
            {World()}
            {InsertLine("w-L1")}
            DECLARE @line int = SCOPE_IDENTITY();
            {InsertPayment(1, "NULL", "@line", 2)}
            UPDATE [OutgoingPayments] SET [RejectReason] = 4 WHERE [Id] = SCOPE_IDENTITY();
            SELECT [RejectReason] FROM [OutgoingPayments] WHERE [ReturnOfLineId] = @line;
            """)).ShouldBe(4);

        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertLine("w-L1")}
            DECLARE @line int = SCOPE_IDENTITY();
            {InsertPayment(1, "NULL", "@line", 2)}
            UPDATE [OutgoingPayments] SET [RejectReason] = 5 WHERE [Id] = SCOPE_IDENTITY();
            """));
        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain("CK_OutgoingPayments_RejectReason");
    }

    [Test]
    public async Task StatementLine_TakesStatuses5And6_Reasons8To11_AndMethod4()
    {
        (await RunAsync($"""
            {World()}
            {InsertLine("w-L1")}
            DECLARE @receipt int = SCOPE_IDENTITY();
            {InsertLine("w-L2")}
            {InsertLine("w-L3")}
            {InsertLine("w-L4")}
            UPDATE [BankStatementLines] SET [Status] = 6 WHERE [Id] = @receipt;
            UPDATE [BankStatementLines] SET [Status] = 5, [UnmatchedReason] = 11 WHERE [OutsideId] = N'w-L2';
            UPDATE [BankStatementLines] SET [Status] = 2, [UnmatchedReason] = 8 WHERE [OutsideId] = N'w-L3';
            UPDATE [BankStatementLines]
               SET [Status] = 1, [MatchMethod] = 4, [MatchedAt] = SYSDATETIMEOFFSET(), [RelatedLineId] = @receipt
             WHERE [OutsideId] = N'w-L4';
            SELECT COUNT(*) FROM [BankStatementLines]
             WHERE [TenantId] = @a_tenant AND ([Status] IN (5, 6) OR [UnmatchedReason] = 8 OR [MatchMethod] = 4);
            """)).ShouldBe(4);
    }

    [TestCase("[Status]", "7", "CK_BankStatementLines_Status")]
    [TestCase("[UnmatchedReason]", "13", "CK_BankStatementLines_UnmatchedReason")]
    [TestCase("[MatchMethod]", "5", "CK_BankStatementLines_MatchMethod")]
    public async Task StatementLineValueOutsideItsWidenedCheck_IsRejected(string column, string value, string check)
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertLine("w-L1")}
            UPDATE [BankStatementLines] SET {column} = {value} WHERE [OutsideId] = N'w-L1';
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain(check);
    }

    [Test]
    public async Task AMatchWithAccountAndTransaction_Inserts()
    {
        (await RunAsync($"""
            {World()}
            {InsertLine("w-L1")}
            UPDATE [BankStatementLines]
               SET [Status] = 1, [MatchedAccountId] = @a_bank, [MatchedTransactionId] = @a_buy, [MatchMethod] = 0, [MatchedAt] = SYSDATETIMEOFFSET()
             WHERE [OutsideId] = N'w-L1';
            SELECT [Status] FROM [BankStatementLines] WHERE [OutsideId] = N'w-L1' AND [TenantId] = @a_tenant;
            """)).ShouldBe(1);
    }

    [TestCase("@a_bank", "NULL", "@receipt")]
    [TestCase("NULL", "@a_buy", "@receipt")]
    [TestCase("NULL", "NULL", "NULL")]
    public async Task AMatchWithoutBothOrARelatedLine_ViolatesTheMatchCheck(string account, string transaction, string related)
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertLine("w-L1")}
            DECLARE @receipt int = SCOPE_IDENTITY();
            {InsertLine("w-L2")}
            UPDATE [BankStatementLines]
               SET [Status] = 1, [MatchedAccountId] = {account}, [MatchedTransactionId] = {transaction}, [MatchMethod] = 2,
                   [MatchedAt] = SYSDATETIMEOFFSET(), [RelatedLineId] = {related}
             WHERE [OutsideId] = N'w-L2';
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain("CK_BankStatementLines_Match");
    }

    [Test]
    public async Task AReturnedLine_WithAMatchColumn_ViolatesTheMatchCheck()
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertLine("w-L1")}
            UPDATE [BankStatementLines] SET [Status] = 6, [MatchMethod] = 4 WHERE [OutsideId] = N'w-L1';
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain("CK_BankStatementLines_Match");
    }

    [Test]
    public async Task EfRoundTrip_KeepsAReceiptReturn_AndALineMatchedWithNoAccount()
    {
        await using var db = await ScratchSchemaDatabase.CreateAsync("BankReturnsEf");
        await db.ApplyAllAsync();
        var ids = (await db.ReadAsync($"""
            {World()}
            SELECT @a_tenant, @cma;
            """)).Single().Select(Convert.ToInt32).ToArray();
        var (tenantId, clientMoneyId) = (ids[0], ids[1]);
        var at = new DateTimeOffset(2026, 11, 21, 4, 0, 0, TimeSpan.Zero);
        var receipt = BankStatementLine.Receive(
            tenantId, clientMoneyId, "simulator", "w-L1", new DateOnly(2026, 11, 21), BankLineDirection.Credit, 250.5m, "USD",
            "J SMITH", "****4200", "Gift", at);
        receipt.CreatedBy = "test";
        receipt.MarkUnmatched(UnmatchedReason.NoReference);
        await using (var context = OpenDb(db.ConnectionString))
        {
            context.BankStatementLines.Add(receipt);
            await context.SaveChangesAsync();
        }

        Guid instruction;
        await using (var context = OpenDb(db.ConnectionString))
        {
            var line = await context.BankStatementLines.SingleAsync(row => row.Id == receipt.Id);
            var clientMoney = await context.ClientMoneyAccounts.SingleAsync(row => row.Id == clientMoneyId);
            var payment = OutgoingPayment.ForReceiptReturn(line, clientMoney);
            instruction = payment.InstructionPublicId;
            payment.CreatedBy = "test";
            context.OutgoingPayments.Add(payment);
            line.MarkReturned();
            var debit = BankStatementLine.Receive(
                tenantId, clientMoneyId, "simulator", "w-L2", new DateOnly(2026, 11, 21), BankLineDirection.Debit, 250.5m, "USD",
                "J SMITH", "****4200", null, at, endToEndId: instruction.ToString());
            debit.CreatedBy = "test";
            debit.MarkReturnPaid(line.Id, at);
            context.BankStatementLines.Add(debit);
            await context.SaveChangesAsync();
        }

        await using (var context = OpenDb(db.ConnectionString))
        {
            var payment = await context.OutgoingPayments.AsNoTracking().SingleAsync(row => row.ReturnOfLineId == receipt.Id);
            payment.Kind.ShouldBe(OutgoingPaymentKind.ReceiptReturn);
            payment.WithdrawalId.ShouldBeNull();
            payment.InstructionPublicId.ShouldBe(instruction);
            payment.Reference.ShouldBeNull();
            payment.Amount.ShouldBe(250.5m);

            var returned = await context.BankStatementLines.AsNoTracking().SingleAsync(row => row.Id == receipt.Id);
            returned.Status.ShouldBe(BankStatementLineStatus.Returned);
            var debit = await context.BankStatementLines.AsNoTracking().SingleAsync(row => row.OutsideId == "w-L2");
            debit.Status.ShouldBe(BankStatementLineStatus.Matched);
            debit.MatchMethod.ShouldBe(StatementLineMatchMethod.Payment);
            debit.MatchedAccountId.ShouldBeNull();
            debit.RelatedLineId.ShouldBe(receipt.Id);
        }
    }

    /// <summary>A tenant (<c>@a_*</c> from <see cref="ScratchSchemaDatabase.TenantRows"/>) with a USD client money
    /// account <c>@cma</c> and a nominated bank account <c>@nba</c> of <c>@a_customer</c>.</summary>
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

    private static string InsertPayment(int kind, string withdrawal, string line, int status) => $"""
        INSERT INTO [OutgoingPayments]
            ([PublicId], [TenantId], [ClientMoneyAccountId], [Connection], [Kind], [InstructionPublicId], [WithdrawalId], [ReturnOfLineId],
             [Amount], [Currency], [Status], [Created], [CreatedBy])
        VALUES (NEWID(), @a_tenant, @cma, N'simulator', {kind}, NEWID(), {withdrawal}, {line}, 300, 'USD', {status}, SYSDATETIMEOFFSET(), N'test');
        """;

    private static string InsertLine(string outsideId) => $"""
        INSERT INTO [BankStatementLines]
            ([PublicId], [TenantId], [ClientMoneyAccountId], [Connection], [OutsideId], [ValueDate], [Direction], [Amount], [Currency],
             [ReceivedAt], [Status], [Created], [CreatedBy])
        VALUES (NEWID(), @a_tenant, @cma, N'simulator', N'{outsideId}', '2026-11-21', 0, 300, 'USD', SYSDATETIMEOFFSET(), 0,
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
