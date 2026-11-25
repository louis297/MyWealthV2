using Microsoft.Data.SqlClient;
using MyWealthV2.Schema;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

/// <summary>Adjustment review §6: <c>AdjustmentRequests</c> and Transaction source kind 11 (script 0058).</summary>
[Parallelizable]
public class AdjustmentRequestsSchemaTests
{
    private const string Script = "0058_adjustment_requests.sql";

    [Test]
    public void SchemaDirectory_Includes0058AdjustmentRequestsScript()
    {
        new FileSchemaScriptSource(ScratchSchemaDatabase.SchemaDirectory()).GetScripts()
            .Select(script => script.Name)
            .ShouldContain(Script);
    }

    [Test]
    public async Task Apply_AddsAdjustmentRequestsWithItsColumns()
    {
        var rows = await ScratchSchemaDatabase.ReadAsync(SchemaDatabaseSetup.ConnectionString, """
            SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_NAME = N'AdjustmentRequests'
            ORDER BY COLUMN_NAME
            """);

        rows.Select(row => $"{row[0]}|{row[1]}|{row[2]}").ShouldBe(
        [
            "AccountId|int|NO",
            "CashAmount|decimal|YES",
            "CostChange|decimal|YES",
            "Created|datetimeoffset|NO",
            "CreatedBy|nvarchar|NO",
            "DecisionAction|int|YES",
            "DecisionAt|datetimeoffset|YES",
            "DecisionByUserId|int|YES",
            "DecisionNote|nvarchar|YES",
            "Id|int|NO",
            "InstrumentId|int|YES",
            "LastModified|datetimeoffset|YES",
            "LastModifiedBy|nvarchar|YES",
            "Memo|nvarchar|NO",
            "PublicId|uniqueidentifier|NO",
            "QuantityChange|decimal|YES",
            "Reason|int|NO",
            "Reference|nvarchar|YES",
            "RequestedAt|datetimeoffset|NO",
            "RequestedByUserId|int|NO",
            "RowVersion|timestamp|NO",
            "Status|int|NO",
            "TenantId|int|NO",
            "TransactionId|int|YES"
        ]);
    }

    [Test]
    public async Task ARequestedRow_WithCash_AnotherWithAHolding_AndAnApprovedRow_Insert()
    {
        (await RunAsync($"""
            {Insert()}
            {Insert(cash: "NULL", instrument: "@a_instrument", quantity: "10", cost: "0")}
            {InsertAdjustment}
            {Insert(status: "1", decisionAction: "1", decidedBy: "@a_other_admin", transaction: "@adjustment")}
            {Insert(status: "2", decisionAction: "2", decidedBy: "@a_other_admin", note: "N'Wrong account'")}
            {Insert(status: "3", decisionAction: "3", decidedBy: "@a_admin")}
            SELECT COUNT(*) FROM [AdjustmentRequests];
            """)).ShouldBe(5);
    }

    [TestCase("cash", "NULL", "CK_AdjustmentRequests_Part")]
    [TestCase("cash", "0", "CK_AdjustmentRequests_Part")]
    [TestCase("reason", "4", "CK_AdjustmentRequests_Reason")]
    [TestCase("status", "4", "CK_AdjustmentRequests_Status")]
    public async Task ARowViolatingACheck_IsRefused(string column, string value, string constraint)
    {
        var values = new Dictionary<string, string> { [column] = value };

        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync(Insert(
            cash: values.GetValueOrDefault("cash", "-5"),
            reason: values.GetValueOrDefault("reason", "0"),
            status: values.GetValueOrDefault("status", "0"))));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain(constraint);
    }

    [TestCase("NULL", "10", "0")]
    [TestCase("@a_instrument", "NULL", "0")]
    [TestCase("@a_instrument", "10", "NULL")]
    [TestCase("@a_instrument", "0", "0")]
    public async Task AHoldingPart_WithAColumnMissingOrNoChange_IsRefused(string instrument, string quantity, string cost)
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync(Insert(
            instrument: instrument, quantity: quantity, cost: cost)));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain("CK_AdjustmentRequests_Holding");
    }

    [Test]
    public async Task ADecisionOnARequestedRow_IsRefused()
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync(Insert(
            status: "0", decisionAction: "2", decidedBy: "@a_other_admin")));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain("CK_AdjustmentRequests_Decision");
    }

    [Test]
    public async Task AnEndStatusWithNoDecision_OrADifferentDecision_IsRefused()
    {
        var none = await Should.ThrowAsync<SqlException>(() => RunAsync(Insert(status: "3")));
        none.Number.ShouldBe(547);
        none.Message.ShouldContain("CK_AdjustmentRequests_Decision");

        var other = await Should.ThrowAsync<SqlException>(() => RunAsync(Insert(
            status: "3", decisionAction: "2", decidedBy: "@a_other_admin")));
        other.Number.ShouldBe(547);
        other.Message.ShouldContain("CK_AdjustmentRequests_Decision");
    }

    [Test]
    public async Task AnApprovedRowWithNoTransaction_OrARejectedRowWithOne_IsRefused()
    {
        var none = await Should.ThrowAsync<SqlException>(() => RunAsync(Insert(
            status: "1", decisionAction: "1", decidedBy: "@a_other_admin")));
        none.Number.ShouldBe(547);
        none.Message.ShouldContain("CK_AdjustmentRequests_Transaction");

        var rejected = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {InsertAdjustment}
            {Insert(status: "2", decisionAction: "2", decidedBy: "@a_other_admin", note: "N'No'", transaction: "@adjustment")}
            """));
        rejected.Number.ShouldBe(547);
        rejected.Message.ShouldContain("CK_AdjustmentRequests_Transaction");
    }

    [TestCase("1")]
    [TestCase("2")]
    public async Task AnApprovalOrARejection_ByTheRequester_IsRefused(string action)
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {InsertAdjustment}
            {Insert(status: action, decisionAction: action, decidedBy: "@a_admin", note: "N'Mine'",
                transaction: action == "1" ? "@adjustment" : "NULL")}
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain("CK_AdjustmentRequests_FourEyes");
    }

    [Test]
    public async Task OneTransaction_BelongsToOneRequestOnly()
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {InsertAdjustment}
            {Insert(status: "1", decisionAction: "1", decidedBy: "@a_other_admin", transaction: "@adjustment")}
            {Insert(status: "1", decisionAction: "1", decidedBy: "@a_other_admin", transaction: "@adjustment")}
            """));

        exception.Number.ShouldBe(2601);
        exception.Message.ShouldContain("UX_AdjustmentRequests_TransactionId");
    }

    [Test]
    public async Task ARequest_WhoseTenantDiffersFromItsAccounts_ViolatesTheCompositeForeignKey()
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {ScratchSchemaDatabase.TenantRows("b", Guid.NewGuid().ToString("N")[..12])}
            {Insert(account: "@b_brokerage")}
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain("FK_AdjustmentRequests_Accounts_TenantId_AccountId");
    }

    [Test]
    public async Task SourceKind11_NeedsASourcePublicId_And12Violates()
    {
        (await RunAsync($"""
            {InsertTransaction(11, "NEWID()")}
            SELECT [SourceKind] FROM [Transactions] WHERE [Id] = SCOPE_IDENTITY();
            """)).ShouldBe(11);

        var noId = await Should.ThrowAsync<SqlException>(() => RunAsync(InsertTransaction(11, "NULL")));
        noId.Number.ShouldBe(547);
        noId.Message.ShouldContain("CK_Transactions_SourcePublicId");

        var twelve = await Should.ThrowAsync<SqlException>(() => RunAsync(InsertTransaction(12, "NEWID()")));
        twelve.Number.ShouldBe(547);
        twelve.Message.ShouldContain("CK_Transactions_SourceKind");
    }

    [Test]
    public async Task Apply_ChangesNoExistingRow()
    {
        (await RunAsync("SELECT COUNT(*) FROM [AdjustmentRequests];")).ShouldBe(0);
        (await RunAsync("SELECT COUNT(*) FROM [Transactions] WHERE [SourceKind] = 11 AND [TenantId] <> @a_tenant;")).ShouldBe(0);
    }

    private const string InsertAdjustment = """
        INSERT INTO [Transactions] ([PublicId], [TenantId], [Type], [SourceKind], [SourcePublicId], [BookedAt], [Created], [CreatedBy])
        VALUES (NEWID(), @a_tenant, 17, 11, NEWID(), SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'test');
        DECLARE @adjustment int = SCOPE_IDENTITY();
        """;

    private static string InsertTransaction(int sourceKind, string sourcePublicId) => $"""
        INSERT INTO [Transactions] ([PublicId], [TenantId], [Type], [SourceKind], [SourcePublicId], [BookedAt], [Created], [CreatedBy])
        VALUES (NEWID(), @a_tenant, 0, {sourceKind}, {sourcePublicId}, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'test');
        """;

    private static string Insert(
        string account = "@a_brokerage",
        string reason = "0",
        string cash = "-5",
        string instrument = "NULL",
        string quantity = "NULL",
        string cost = "NULL",
        string status = "0",
        string decisionAction = "NULL",
        string? decidedBy = null,
        string note = "NULL",
        string transaction = "NULL")
    {
        var decided = decidedBy is not null;
        return $"""
            INSERT INTO [AdjustmentRequests]
                ([PublicId], [TenantId], [AccountId], [Reason], [Memo], [Reference], [CashAmount], [InstrumentId], [QuantityChange],
                 [CostChange], [Status], [RequestedByUserId], [RequestedAt], [DecisionAction], [DecisionByUserId], [DecisionAt],
                 [DecisionNote], [TransactionId], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, {account}, {reason}, N'Correction', NULL, {cash}, {instrument}, {quantity},
                 {cost}, {status}, @a_admin, SYSDATETIMEOFFSET(), {decisionAction}, {decidedBy ?? "NULL"},
                 {(decided ? "SYSDATETIMEOFFSET()" : "NULL")}, {note}, {transaction}, SYSDATETIMEOFFSET(), N'test');
            """;
    }

    /// <summary>Runs <paramref name="statement"/> after one tenant's rows and two TenantAdmins, then rolls back.</summary>
    private static async Task<object?> RunAsync(string statement)
    {
        await using var connection = new SqlConnection(SchemaDatabaseSetup.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();
        try
        {
            var code = Guid.NewGuid().ToString("N")[..12];
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = ScratchSchemaDatabase.TenantRows("a", code) + $"""

                INSERT INTO [AspNetUsers] ([Id], [EmailConfirmed], [PhoneNumberConfirmed], [TwoFactorEnabled], [LockoutEnabled], [AccessFailedCount], [TenantId])
                VALUES (N'{code}-admin', 1, 0, 0, 0, 0, @a_tenant), (N'{code}-admin2', 1, 0, 0, 0, 0, @a_tenant);
                INSERT INTO [Users] ([PublicId], [TenantId], [Name], [Email], [Role], [Status], [AdviserId], [IdentityUserId], [Created], [LastModified])
                VALUES (NEWID(), @a_tenant, N'Tess Admin', N'tess@{code}.example', 1, 1, NULL, N'{code}-admin', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
                DECLARE @a_admin int = SCOPE_IDENTITY();
                INSERT INTO [Users] ([PublicId], [TenantId], [Name], [Email], [Role], [Status], [AdviserId], [IdentityUserId], [Created], [LastModified])
                VALUES (NEWID(), @a_tenant, N'Tom Admin', N'tom@{code}.example', 1, 1, NULL, N'{code}-admin2', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
                DECLARE @a_other_admin int = SCOPE_IDENTITY();

                """ + statement;
            return await command.ExecuteScalarAsync();
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }
}
