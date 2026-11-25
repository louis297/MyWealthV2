using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyWealthV2.Domain.Custody;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.ValueObjects;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Schema;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

/// <summary>In-kind transfers §6.1: safekeeping accounts, the custodian feed, custody movements, in-kind transfers,
/// the in-kind transfer detail, the order's safekeeping account and source kind 10 (script 0055).</summary>
public class CustodianSchemaTests
{
    private const string Script = "0055_custodian.sql";

    [Test]
    public void SchemaDirectory_Includes0055Script()
    {
        new FileSchemaScriptSource(ScratchSchemaDatabase.SchemaDirectory()).GetScripts()
            .Select(script => script.Name)
            .ShouldContain(Script);
    }

    [Test]
    public async Task ASecondActiveSafekeepingAccountInOneCurrency_Violates()
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {Safekeeping("a", "USD", active: true)}
            {Safekeeping("a", "USD", active: true)}
            """));

        exception.Number.ShouldBe(2601);
        exception.Message.ShouldContain("UX_SafekeepingAccounts_TenantId_Currency_Active");
    }

    [Test]
    public async Task ASecondSafekeepingAccount_InAnotherCurrency_OrInactive_Inserts()
    {
        (await RunAsync($"""
            {World()}
            {Safekeeping("a", "USD", active: true)}
            {Safekeeping("a", "NZD", active: true)}
            {Safekeeping("a", "USD", active: false)}
            SELECT COUNT(*) FROM [SafekeepingAccounts] WHERE [TenantId] = @a_tenant;
            """)).ShouldBe(3);
    }

    [Test]
    public async Task OneNumberOnTwoActiveSafekeepingAccounts_Violates()
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {ScratchSchemaDatabase.TenantRows("b", Guid.NewGuid().ToString("N")[..12])}
            {Safekeeping("a", "USD", active: true, number: "N'SK-SHARED'")}
            {Safekeeping("b", "USD", active: true, number: "N'sk-shared'")}
            """));

        exception.Number.ShouldBe(2601);
        exception.Message.ShouldContain("UX_SafekeepingAccounts_AccountNumber_Active");
    }

    [Test]
    public async Task AMovementTwiceOnOneConnectionAndOutsideId_Violates()
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {Safekeeping("a", "USD", active: true)}
            {Movement(outsideId: "N'w-M1'")}
            {Movement(outsideId: "N'w-M1'")}
            """));

        exception.Number.ShouldBe(2627);
        exception.Message.ShouldContain("UX_CustodyMovements_Connection_OutsideId");
    }

    [TestCase("1", "0", "NULL", "NULL", "CK_CustodyMovements_Match")]
    [TestCase("1", "1", "NULL", "NULL", "CK_CustodyMovements_Match")]
    [TestCase("1", "2", "NULL", "NULL", "CK_CustodyMovements_Match")]
    [TestCase("1", "NULL", "NULL", "NULL", "CK_CustodyMovements_MatchMethod")]
    [TestCase("0", "1", "NULL", "NULL", "CK_CustodyMovements_MatchMethod")]
    [TestCase("2", "NULL", "NULL", "NULL", "CK_CustodyMovements_UnmatchedReason")]
    [TestCase("0", "NULL", "3", "NULL", "CK_CustodyMovements_UnmatchedReason")]
    [TestCase("4", "NULL", "NULL", "NULL", "CK_CustodyMovements_Status")]
    [TestCase("2", "NULL", "10", "NULL", "CK_CustodyMovements_UnmatchedReason")]
    [TestCase("0", "NULL", "NULL", "0", "CK_CustodyMovements_Quantity")]
    public async Task AMovementOutsideTheChecks_Violates(string status, string method, string reason, string quantity, string constraint)
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {Safekeeping("a", "USD", active: true)}
            {Movement(status: status, method: method, reason: reason, quantity: quantity == "NULL" ? "4" : quantity)}
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain(constraint);
    }

    [Test]
    public async Task AMovementMatchedToAFill_Inserts_AndASecondOnTheSameFill_Violates()
    {
        (await RunAsync($"""
            {World()}
            {Safekeeping("a", "USD", active: true)}
            {Fill()}
            {Movement(status: "1", method: "1", fill: "@fill")}
            SELECT COUNT(*) FROM [CustodyMovements] WHERE [OrderFillId] = @fill;
            """)).ShouldBe(1);

        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {Safekeeping("a", "USD", active: true)}
            {Fill()}
            {Movement(status: "1", method: "1", fill: "@fill")}
            {Movement(status: "1", method: "1", fill: "@fill")}
            """));
        exception.Number.ShouldBe(2601);
        exception.Message.ShouldContain("UX_CustodyMovements_OrderFillId");
    }

    [TestCase("0")]
    [TestCase("1")]
    [TestCase("2")]
    [TestCase("4")]
    [TestCase("5")]
    public async Task ATransfer_InAStatusWithItsColumns_Inserts(string status)
    {
        (await RunAsync($"""
            {World()}
            {Safekeeping("a", "USD", active: true)}
            {Transfer(status: status)}
            SELECT [Status] FROM [InKindTransfers] WHERE [Id] = @transfer;
            """)).ShouldBe(int.Parse(status));
    }

    [Test]
    public async Task ACompletedTransferWithItsTransaction_Inserts()
    {
        (await RunAsync($"""
            {World()}
            {Safekeeping("a", "USD", active: true)}
            {Transfer(status: "3", transaction: "@a_buy")}
            SELECT [TransactionId] FROM [InKindTransfers] WHERE [Id] = @transfer;
            """)).ShouldNotBeNull();
    }

    [TestCase("status: 3", "CK_InKindTransfers_Completed")]
    [TestCase("status: 2, transaction: @a_buy", "CK_InKindTransfers_Completed")]
    [TestCase("direction: 0, cost: NULL", "CK_InKindTransfers_Cost")]
    [TestCase("direction: 1, cost: 5", "CK_InKindTransfers_Cost")]
    [TestCase("direction: 1, acquiredOn: '2024-01-01'", "CK_InKindTransfers_Cost")]
    [TestCase("direction: 0, cost: -1", "CK_InKindTransfers_Cost")]
    [TestCase("status: 1, decidedBy: @a_adviser", "CK_InKindTransfers_FourEyes")]
    [TestCase("status: 4, decidedBy: @a_adviser", "CK_InKindTransfers_FourEyes")]
    [TestCase("status: 1, safekeeping: NULL", "CK_InKindTransfers_SafekeepingAccount")]
    [TestCase("status: 6", "CK_InKindTransfers_FailureReason")]
    [TestCase("status: 7", "CK_InKindTransfers_SetAside")]
    [TestCase("status: 8", "CK_InKindTransfers_Status")]
    [TestCase("quantity: 0", "CK_InKindTransfers_Quantity")]
    public async Task ATransferOutsideTheChecks_Violates(string change, string constraint)
    {
        var values = change.Split(", ").Select(part => part.Split(": ")).ToDictionary(pair => pair[0], pair => pair[1]);
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {Safekeeping("a", "USD", active: true)}
            {Transfer(
                status: values.GetValueOrDefault("status", "0"),
                direction: values.GetValueOrDefault("direction", "1"),
                cost: values.GetValueOrDefault("cost"),
                acquiredOn: values.GetValueOrDefault("acquiredOn", "NULL"),
                decidedBy: values.GetValueOrDefault("decidedBy"),
                safekeeping: values.GetValueOrDefault("safekeeping", "@a_safekeeping"),
                transaction: values.GetValueOrDefault("transaction", "NULL"),
                quantity: values.GetValueOrDefault("quantity", "4"))}
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain(constraint);
    }

    [Test]
    public async Task SourceKind10_NeedsASourcePublicId_And12Violates()
    {
        (await RunAsync($"""
            {World()}
            {InsertTransaction(10, "NEWID()")}
            SELECT [SourceKind] FROM [Transactions] WHERE [Id] = SCOPE_IDENTITY();
            """)).ShouldBe(10);

        var noId = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertTransaction(10, "NULL")}
            """));
        noId.Number.ShouldBe(547);
        noId.Message.ShouldContain("CK_Transactions_SourcePublicId");

        var twelve = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertTransaction(12, "NEWID()")}
            """));
        twelve.Number.ShouldBe(547);
        twelve.Message.ShouldContain("CK_Transactions_SourceKind");
    }

    [Test]
    public async Task AnInKindTransferDetail_IsOnePerTransaction()
    {
        var twice = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertTransaction(10, "NEWID()")}
            DECLARE @posting int = SCOPE_IDENTITY();
            {Detail("@posting")}
            {Detail("@posting")}
            """));

        twice.Number.ShouldBe(2627);
    }

    [Test]
    public async Task AnOrderWithASafekeepingAccountAndNoNumber_Violates()
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {Safekeeping("a", "USD", active: true)}
            {Fill()}
            UPDATE [Orders] SET [SafekeepingAccountId] = (SELECT MIN([Id]) FROM #sk) WHERE [Id] = @order;
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain("CK_Orders_SafekeepingAccount");
    }

    [Test]
    public async Task EfRoundTrip_KeepsASafekeepingAccount_ItsFeed_AMovement_ATransfer_AndTheDetail()
    {
        await using var db = await ScratchSchemaDatabase.CreateAsync("CustodianEf");
        await db.ApplyAllAsync();
        var row = (await db.ReadAsync($"""
            {ScratchSchemaDatabase.TenantRows("a", Guid.NewGuid().ToString("N")[..12])}
            SELECT @a_tenant, @a_brokerage, @a_instrument, @a_adviser, @a_customer;
            """)).Single();
        var ids = row.Select(Convert.ToInt32).ToArray();
        var (tenantId, accountId, instrumentId, requester, approver) = (ids[0], ids[1], ids[2], ids[3], ids[4]);
        var at = new DateTimeOffset(2026, 11, 24, 1, 0, 0, TimeSpan.Zero);
        int safekeepingId, transferId, movementId, transactionId;
        await using (var context = OpenDb(db.ConnectionString))
        {
            var usd = await context.Set<Currency>().SingleAsync(c => c.Code == "USD");
            var safekeeping = SafekeepingAccount.Create(tenantId, usd, "Simulator Custody", "Nominees", "SK-EF-1", "simulator");
            safekeeping.CreatedBy = "test";
            context.Set<SafekeepingAccount>().Add(safekeeping);
            await context.SaveChangesAsync();
            safekeepingId = safekeeping.Id;
            var cursor = CustodianFeedCursor.Start(tenantId, safekeepingId);
            cursor.StorePage("w:1", at);
            context.Set<CustodianFeedCursor>().Add(cursor);
            context.Set<CustodianPosition>().Add(CustodianPosition.Create(tenantId, safekeepingId, "VTI", -2.5m));

            var transfer = InKindTransfer.Request(tenantId, accountId, instrumentId, TransferDirection.In, 100m, "USD", 9500m,
                new DateOnly(2024, 3, 18), true, "Harbour Brokers", "HB-0044172", "J Smith", "memo", requester, at);
            transfer.CreatedBy = "test";
            transfer.Approve(approver, at, "ok", safekeepingId);
            transfer.RecordAttempt(at);
            transfer.Accept("w-T1", at);
            context.Set<InKindTransfer>().Add(transfer);
            await context.SaveChangesAsync();
            transferId = transfer.Id;

            var posting = Transaction.PostTransferIn(tenantId, accountId, AccountType.Brokerage, instrumentId, 100m, 9500m, "USD", at,
                TransactionSource.InKindTransfer(transfer.PublicId),
                new InKindTransferOrigin(transfer.PublicId, "Harbour Brokers", "HB-0044172", true, new DateOnly(2024, 3, 18),
                    new DateOnly(2026, 11, 24), "w-M1"));
            posting.CreatedBy = "test";
            posting.SecurityLegs.ForEach(leg => leg.CreatedBy = "test");
            context.Set<Transaction>().Add(posting);
            await context.SaveChangesAsync();
            transactionId = posting.Id;

            var movement = CustodyMovement.Receive(tenantId, safekeepingId, "simulator", "w-M1", CustodyMovementDirection.In,
                CustodyMovementKind.Transfer, "VTI", "USD", instrumentId, 100m, new DateOnly(2026, 11, 24),
                transfer.PublicId.ToString(), null, null, "Harbour Brokers", "HB-0044172", null, at);
            movement.CreatedBy = "test";
            movement.MatchToTransfer(accountId, transactionId, transferId);
            transfer.Complete(transactionId, at);
            context.Set<CustodyMovement>().Add(movement);
            await context.SaveChangesAsync();
            movementId = movement.Id;
        }

        await using (var context = OpenDb(db.ConnectionString))
        {
            var safekeeping = await context.Set<SafekeepingAccount>().AsNoTracking().SingleAsync(s => s.Id == safekeepingId);
            (safekeeping.Currency, safekeeping.AccountNumber, safekeeping.IsActive).ShouldBe(("USD", "SK-EF-1", true));
            safekeeping.RowVersion.ShouldNotBeEmpty();
            var cursor = await context.Set<CustodianFeedCursor>().AsNoTracking().SingleAsync(c => c.SafekeepingAccountId == safekeepingId);
            (cursor.Cursor, cursor.PositionsAt).ShouldBe(("w:1", (DateTimeOffset?)at));
            (await context.Set<CustodianPosition>().AsNoTracking().SingleAsync(p => p.SafekeepingAccountId == safekeepingId)).Quantity
                .ShouldBe(-2.5m);
            var transfer = await context.Set<InKindTransfer>().AsNoTracking().SingleAsync(t => t.Id == transferId);
            (transfer.Status, transfer.Cost, transfer.SafekeepingAccountId, transfer.CustodianTransferId, transfer.TransactionId)
                .ShouldBe((InKindTransferStatus.Completed, (decimal?)9500m, (int?)safekeepingId, "w-T1", (int?)transactionId));
            (transfer.DecisionAction, transfer.Attempts).ShouldBe(((InKindTransferDecision?)InKindTransferDecision.Approve, 1));
            var movement = await context.Set<CustodyMovement>().AsNoTracking().SingleAsync(m => m.Id == movementId);
            (movement.Status, movement.MatchMethod, movement.InKindTransferId, movement.Kind)
                .ShouldBe((CustodyMovementStatus.Matched, (CustodyMatchMethod?)CustodyMatchMethod.Transfer, (int?)transferId,
                    (CustodyMovementKind?)CustodyMovementKind.Transfer));
            var posting = await context.Set<Transaction>().AsNoTracking().Include(t => t.InKindTransferDetail)
                .SingleAsync(t => t.Id == transactionId);
            (posting.Source.Kind, posting.InKindTransferDetail!.CounterpartyAccountMasked, posting.InKindTransferDetail.ChangesBeneficialOwner)
                .ShouldBe((TransactionSourceKind.InKindTransfer, "****4172", true));
        }
    }

    /// <summary>A tenant (<c>@a_*</c>) and <c>#sk</c>, the safekeeping accounts inserted.</summary>
    private static string World() => ScratchSchemaDatabase.TenantRows("a", Guid.NewGuid().ToString("N")[..12]) + """

        CREATE TABLE #sk ([Id] int);
        """;

    /// <summary>The first safekeeping account <see cref="Safekeeping"/> inserted.</summary>
    private const string Sk = "(SELECT MIN([Id]) FROM #sk)";

    private static string Safekeeping(string tenant, string currency, bool active, string? number = null) => $"""
        INSERT INTO [SafekeepingAccounts]
            ([PublicId], [TenantId], [Currency], [CustodianName], [AccountName], [AccountNumber], [CustodianConnection], [IsActive], [Created], [CreatedBy])
        VALUES (NEWID(), @{tenant}_tenant, '{currency}', N'Simulator Custody', N'Nominees',
                {number ?? $"N'SK-{Guid.NewGuid().ToString("N")[..12]}'"}, N'simulator', {(active ? 1 : 0)}, SYSDATETIMEOFFSET(), N'test');
        INSERT INTO #sk VALUES (SCOPE_IDENTITY());
        """;

    private static string Fill() => """
        INSERT INTO [Orders]
            ([PublicId], [TenantId], [AccountId], [InstrumentId], [Side], [Type], [TimeInForce], [Quantity], [Currency],
             [ReferencePrice], [ReferencePriceDate], [PriceLimit], [Connection], [Status], [FilledQuantity],
             [PostedGross], [FeeCharged], [ReservedCash], [ReservedQuantity], [PlacedByUserId], [PlacedAt], [Attempts], [Created], [CreatedBy])
        VALUES (NEWID(), @a_tenant, @a_brokerage, @a_instrument, 0, 0, 0, 10, 'USD',
                100, '2026-11-23', 105, N'simulator', 0, 0, 0, 0, 0, 0, @a_adviser, SYSDATETIMEOFFSET(), 0, SYSDATETIMEOFFSET(), N'test');
        DECLARE @order int = SCOPE_IDENTITY();
        INSERT INTO [ExecutionReports] ([PublicId], [Connection], [OutsideId], [Kind], [ReceivedAt], [Status])
        VALUES (NEWID(), N'simulator', CONVERT(nvarchar(100), NEWID()), 0, SYSDATETIMEOFFSET(), 0);
        INSERT INTO [OrderFills] ([PublicId], [TenantId], [OrderId], [ExecutionReportId], [Quantity], [Price], [TradeDate], [SettlementDate], [Status])
        VALUES (NEWID(), @a_tenant, @order, SCOPE_IDENTITY(), 4, 100, '2026-11-23', '2026-11-25', 0);
        DECLARE @fill bigint = SCOPE_IDENTITY();
        """;

    private static string Movement(
        string outsideId = "CONVERT(nvarchar(100), NEWID())",
        string status = "0",
        string method = "NULL",
        string reason = "NULL",
        string quantity = "4",
        string fill = "NULL") => $"""
        INSERT INTO [CustodyMovements]
            ([PublicId], [TenantId], [SafekeepingAccountId], [Connection], [OutsideId], [Direction], [Kind], [Symbol], [Currency],
             [InstrumentId], [Quantity], [SettlementDate], [ReceivedAt], [Status], [MatchMethod], [UnmatchedReason], [OrderFillId],
             [Created], [CreatedBy])
        VALUES (NEWID(), @a_tenant, (SELECT MIN([Id]) FROM #sk), N'simulator', {outsideId}, 0, 1, N'VTI', 'USD',
                @a_instrument, {quantity}, '2026-11-24', SYSDATETIMEOFFSET(), {status}, {method}, {reason}, {fill},
                SYSDATETIMEOFFSET(), N'test');
        """;

    private static string Transfer(
        string status = "0",
        string direction = "1",
        string? cost = null,
        string acquiredOn = "NULL",
        string? decidedBy = null,
        string safekeeping = "@a_safekeeping",
        string transaction = "NULL",
        string quantity = "4")
    {
        var costValue = cost ?? (direction == "0" ? "9500" : "NULL");
        var decided = decidedBy ?? (status is "0" or "8" ? "NULL" : status == "5" ? "@a_adviser" : "@a_customer");
        var action = decided == "NULL" ? "NULL" : status == "4" ? "1" : status == "5" ? "2" : "0";
        var skValue = safekeeping == "@a_safekeeping" ? (status is "0" or "4" ? "NULL" : Sk) : safekeeping;
        return $"""
            INSERT INTO [InKindTransfers]
                ([PublicId], [TenantId], [AccountId], [InstrumentId], [Direction], [Quantity], [Currency], [Cost], [AcquiredOn],
                 [ChangesBeneficialOwner], [CounterpartyName], [CounterpartyAccount], [CounterpartyHolderName], [Status],
                 [RequestedByUserId], [RequestedAt], [DecisionAction], [DecisionByUserId], [DecisionAt], [SafekeepingAccountId],
                 [Attempts], [TransactionId], [CompletedAt], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @a_brokerage, @a_instrument, {direction}, {quantity}, 'USD', {costValue}, {acquiredOn},
                    0, N'Harbour Brokers', N'HB-0044172', N'J Smith', {status},
                    @a_adviser, SYSDATETIMEOFFSET(), {action}, {decided}, {(decided == "NULL" ? "NULL" : "SYSDATETIMEOFFSET()")}, {skValue},
                    0, {transaction}, {(transaction == "NULL" ? "NULL" : "SYSDATETIMEOFFSET()")}, SYSDATETIMEOFFSET(), N'test');
            DECLARE @transfer int = SCOPE_IDENTITY();
            """;
    }

    private static string InsertTransaction(int sourceKind, string sourcePublicId) => $"""
        INSERT INTO [Transactions] ([PublicId], [TenantId], [Type], [SourceKind], [SourcePublicId], [BookedAt], [Created], [CreatedBy])
        VALUES (NEWID(), @a_tenant, 0, {sourceKind}, {sourcePublicId}, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'test');
        """;

    private static string Detail(string transaction) => $"""
        INSERT INTO [TransactionInKindTransferDetails]
            ([TransactionId], [TenantId], [TransferPublicId], [CounterpartyName], [CounterpartyAccountMasked], [ChangesBeneficialOwner],
             [SettlementDate], [CustodianReference])
        VALUES ({transaction}, @a_tenant, NEWID(), N'Harbour Brokers', N'****4172', 0, '2026-11-24', N'w-M1');
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
