using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Orders;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Schema;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

/// <summary>Trade-settlement §6.1: <c>TradeSettlements</c>, the settlement account of an order, the settlement and
/// break columns of a fill, the settlement fields of a report, the trade settlement arm of a payment and of a matched
/// statement line, and the open settlements of a reconciliation run (script 0052).</summary>
public class TradeSettlementSchemaTests
{
    private const string Script = "0052_trade_settlement.sql";

    [Test]
    public void SchemaDirectory_Includes0052Script()
    {
        new FileSchemaScriptSource(ScratchSchemaDatabase.SchemaDirectory()).GetScripts()
            .Select(script => script.Name)
            .ShouldContain(Script);
    }

    [Test]
    public async Task Apply0052_KeepsExistingRows_WithNoSettlementAccount_AndARunWithNoOpenSettlements()
    {
        await using var db = await ScratchSchemaDatabase.CreateAsync("TradeSettlement0052");
        await db.ApplyBeforeAsync(Script);
        await db.ExecuteAsync($"""
            {World()}
            {OrderRow(settlementColumns: false)}
            INSERT INTO [BankReconciliations]
                ([PublicId], [TenantId], [ClientMoneyAccountId], [Currency], [Trigger], [RanAt], [Status], [CustodialCash],
                 [BreakCount], [BreakTotal], [TimingCount], [TimingTotal], [KnownCount], [KnownTotal], [ReservedCash],
                 [PaymentsInFlightCount], [PaymentsInFlightAmount], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @cma, 'USD', 0, SYSDATETIMEOFFSET(), 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                    SYSDATETIMEOFFSET(), N'test');
            """);

        await db.ApplyAllAsync();

        var order = (await db.ReadAsync("SELECT [ClientMoneyAccountId], [SettlementAccountNumber] FROM [Orders];")).ShouldHaveSingleItem();
        (order[0] is DBNull, order[1] is DBNull).ShouldBe((true, true));
        var run = (await db.ReadAsync("SELECT [OpenSettlementsCount], [OpenSettlementsAmount] FROM [BankReconciliations];")).ShouldHaveSingleItem();
        ((int)run[0], (decimal)run[1]).ShouldBe((0, 0m));
    }

    [TestCase("0", "NULL", false, false)]
    [TestCase("1", "NULL", false, false)]
    [TestCase("2", "NULL", true, false)]
    [TestCase("3", "2", false, false)]
    [TestCase("4", "NULL", false, false)]
    [TestCase("5", "NULL", false, true)]
    public async Task ATradeSettlement_InEveryStatus_WithItsColumns_Inserts(string status, string failure, bool completed, bool setAside)
    {
        (await RunAsync($"""
            {World()}
            {Report("w-X9", kind: "5")}
            {Settlement("w-S1", status, failure, completed, setAside)}
            SELECT [Status] FROM [TradeSettlements] WHERE [Id] = SCOPE_IDENTITY();
            """)).ShouldBe(int.Parse(status));
    }

    [Test]
    public async Task ASecondTradeSettlement_ForTheSameConnectionIdAndAccount_Violates()
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {Report("w-X9", kind: "5")}
            {Settlement("w-S1")}
            {Settlement("w-S1")}
            """));

        exception.Number.ShouldBe(2601);
        exception.Message.ShouldContain("UX_TradeSettlements_Connection_BrokerSettlementId_ClientMoneyAccountId");
    }

    [TestCase("status: 6", "CK_TradeSettlements_Status")]
    [TestCase("status: 3", "CK_TradeSettlements_Failed")]
    [TestCase("failure: 0", "CK_TradeSettlements_Failed")]
    [TestCase("status: 3, failure: 3", "CK_TradeSettlements_Failure")]
    [TestCase("status: 2", "CK_TradeSettlements_Completed")]
    [TestCase("completed: 1", "CK_TradeSettlements_Completed")]
    [TestCase("status: 5", "CK_TradeSettlements_SetAside")]
    [TestCase("setAside: 1", "CK_TradeSettlements_SetAside")]
    [TestCase("fillCount: 0", "CK_TradeSettlements_FillCount")]
    public async Task ATradeSettlementOutsideTheChecks_Violates(string change, string constraint)
    {
        var values = Values(change);
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {Report("w-X9", kind: "5")}
            {Settlement(
                "w-S1",
                values.GetValueOrDefault("status", "0"),
                values.GetValueOrDefault("failure", "NULL"),
                values.GetValueOrDefault("completed", "0") == "1",
                values.GetValueOrDefault("setAside", "0") == "1",
                values.GetValueOrDefault("fillCount", "2"))}
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain(constraint);
    }

    [Test]
    public async Task AnOrderWithASettlementAccount_Inserts_AndOneWithOnlyHalfOfIt_Violates()
    {
        (await RunAsync($"""
            {World()}
            {OrderRow(clientMoney: "@cma", accountNumber: "N'SIM-DEMO-USD'")}
            SELECT [SettlementAccountNumber] FROM [Orders] WHERE [Id] = (SELECT [Id] FROM #order);
            """)).ShouldBe("SIM-DEMO-USD");

        var half = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {OrderRow(clientMoney: "@cma")}
            """));
        half.Number.ShouldBe(547);
        half.Message.ShouldContain("CK_Orders_SettlementAccount");
    }

    [Test]
    public async Task AnOrderWithAnotherTenantsClientMoneyAccount_Violates()
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {ScratchSchemaDatabase.TenantRows("b", Guid.NewGuid().ToString("N")[..12])}
            {ClientMoney("b", "cmb")}
            {OrderRow(clientMoney: "@cmb", accountNumber: "N'SIM-B'")}
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain("FK_Orders_ClientMoneyAccounts_TenantId_ClientMoneyAccountId");
    }

    [TestCase("1", "NULL", "@a_buy", "NULL", "NULL")]
    [TestCase("0", "NULL", "NULL", "NULL", "NULL")]
    [TestCase("2", "0", "@a_buy", "@a_fx", "NULL")]
    [TestCase("2", "1", "NULL", "NULL", "NULL")]
    [TestCase("2", "2", "@a_buy", "NULL", "1")]
    public async Task AFill_PendingPostedOrBrokenWithEachOutcome_Inserts(
        string status, string outcome, string transaction, string reversal, string holdReason)
    {
        (await RunAsync($"""
            {World()}
            {OrderRow(status: "2", filled: "4")}
            {Report("w-X1")}
            {Report("w-X4", kind: "4")}
            {Fill(status, transaction, outcome, reversal, holdReason)}
            SELECT [Status] FROM [OrderFills] WHERE [OrderId] = (SELECT [Id] FROM #order);
            """)).ShouldBe(int.Parse(status));
    }

    [Test]
    public async Task AFillResolvedByHand_Inserts_WithThePersonTheTimeAndTheNote()
    {
        (await RunAsync($"""
            {World()}
            {OrderRow(status: "2", filled: "4")}
            {Report("w-X1")}
            {Report("w-X4", kind: "4")}
            {Fill("2", "@a_buy", "3", "NULL", "1", resolved: true)}
            SELECT [BreakNote] FROM [OrderFills] WHERE [OrderId] = (SELECT [Id] FROM #order);
            """)).ShouldBe("Corrected");
    }

    [TestCase("1", "0", "@a_buy", "@a_fx", "NULL", false, "CK_OrderFills_Broken")]
    [TestCase("2", "NULL", "@a_buy", "NULL", "NULL", false, "CK_OrderFills_Broken")]
    [TestCase("2", "4", "@a_buy", "NULL", "NULL", false, "CK_OrderFills_BreakOutcome")]
    [TestCase("2", "0", "@a_buy", "NULL", "NULL", false, "CK_OrderFills_Reversal")]
    [TestCase("2", "2", "@a_buy", "@a_fx", "1", false, "CK_OrderFills_Reversal")]
    [TestCase("2", "2", "@a_buy", "NULL", "NULL", false, "CK_OrderFills_BreakHeld")]
    [TestCase("2", "1", "NULL", "NULL", "0", false, "CK_OrderFills_BreakHeld")]
    [TestCase("2", "2", "@a_buy", "NULL", "3", false, "CK_OrderFills_BreakHoldReason")]
    [TestCase("2", "3", "@a_buy", "NULL", "1", false, "CK_OrderFills_BreakResolved")]
    [TestCase("2", "2", "@a_buy", "NULL", "1", true, "CK_OrderFills_BreakResolved")]
    [TestCase("1", "NULL", "NULL", "NULL", "NULL", false, "CK_OrderFills_Posted")]
    [TestCase("0", "NULL", "@a_buy", "NULL", "NULL", false, "CK_OrderFills_Posted")]
    public async Task AFillOutsideTheBreakChecks_Violates(
        string status, string outcome, string transaction, string reversal, string holdReason, bool resolved, string constraint)
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {OrderRow(status: "2", filled: "4")}
            {Report("w-X1")}
            {Report("w-X4", kind: "4")}
            {Fill(status, transaction, outcome, reversal, holdReason, resolved)}
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain(constraint);
    }

    [Test]
    public async Task TwoFillsBrokenByTheSameReport_Violate()
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {OrderRow(status: "2", filled: "8")}
            {Report("w-X4", kind: "4")}
            CREATE TABLE #break ([Id] bigint);
            INSERT INTO #break SELECT [Id] FROM #report;
            {Report("w-X1")}
            {Fill("2", "NULL", "1", "NULL", "NULL", breakReport: "(SELECT [Id] FROM #break)")}
            {Report("w-X2")}
            {Fill("2", "NULL", "1", "NULL", "NULL", breakReport: "(SELECT [Id] FROM #break)")}
            """));

        exception.Number.ShouldBe(2601);
        exception.Message.ShouldContain("UX_OrderFills_BreakReportId");
    }

    [Test]
    public async Task AFillInATradeSettlement_Inserts_AndASettlementReportWithItsFields_Inserts()
    {
        (await RunAsync($"""
            {World()}
            {OrderRow(status: "2", filled: "4", clientMoney: "@cma", accountNumber: "N'SIM-DEMO-USD'")}
            INSERT INTO [ExecutionReports]
                ([PublicId], [Connection], [OutsideId], [Kind], [SettlementId], [SettlementAccount], [Currency], [SettledDate],
                 [NetAmount], [FillCount], [ReceivedAt], [Status])
            VALUES (NEWID(), N'simulator', N'w-X9', 5, N'w-S1', N'SIM-DEMO-USD', 'USD', '2026-11-25', -400.4800, 1, SYSDATETIMEOFFSET(), 0);
            DELETE FROM #report; INSERT INTO #report VALUES (SCOPE_IDENTITY());
            {Settlement("w-S1")}
            DECLARE @settlement int = SCOPE_IDENTITY();
            {Report("w-X1")}
            {Fill("1", "@a_buy", "NULL", "NULL", "NULL")}
            UPDATE [OrderFills] SET [BrokerSettlementId] = N'w-S1', [TradeSettlementId] = @settlement;
            SELECT COUNT(*) FROM [OrderFills] WHERE [TradeSettlementId] = @settlement;
            """)).ShouldBe(1);
    }

    [TestCase("[Kind] = 5", "CK_ExecutionReports_Kind", false)]
    [TestCase("[Kind] = 6", "CK_ExecutionReports_Kind", true)]
    [TestCase("[Status] = 2, [SkipReason] = 7", "CK_ExecutionReports_SkipReason", false)]
    [TestCase("[Status] = 2, [SkipReason] = 8", "CK_ExecutionReports_SkipReason", true)]
    public async Task AReportsWidenedChecks(string set, string constraint, bool violates)
    {
        var statement = $"""
            {Report("w-X1")}
            UPDATE [ExecutionReports] SET {set} WHERE [OutsideId] = N'w-X1';
            SELECT COUNT(*) FROM [ExecutionReports] WHERE [OutsideId] = N'w-X1';
            """;
        if (!violates)
        {
            (await RunAsync(statement)).ShouldBe(1);
            return;
        }

        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync(statement));
        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain(constraint);
    }

    [Test]
    public async Task ATradeSettlementPayment_Inserts_AndASecondOpenOne_Violates_AndAClosedOneMayFollow()
    {
        (await RunAsync($"""
            {World()}
            {Report("w-X9", kind: "5")}
            {Settlement("w-S1", status: "0")}
            DECLARE @settlement int = SCOPE_IDENTITY();
            {Payment("2", "NULL", "NULL", "@settlement", "4")}
            {Payment("2", "NULL", "NULL", "@settlement", "1")}
            SELECT COUNT(*) FROM [OutgoingPayments] WHERE [TradeSettlementId] = @settlement;
            """)).ShouldBe(2);

        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {Report("w-X9", kind: "5")}
            {Settlement("w-S1", status: "0")}
            DECLARE @settlement int = SCOPE_IDENTITY();
            {Payment("2", "NULL", "NULL", "@settlement", "0")}
            {Payment("2", "NULL", "NULL", "@settlement", "1")}
            """));
        exception.Number.ShouldBe(2601);
        exception.Message.ShouldContain("UX_OutgoingPayments_TradeSettlementId_Open");
    }

    [TestCase("2", "NULL", "NULL", "NULL")]
    [TestCase("2", "@withdrawal", "NULL", "@settlement")]
    [TestCase("0", "@withdrawal", "NULL", "@settlement")]
    [TestCase("1", "NULL", "@line", "@settlement")]
    public async Task APaymentOutsideItsTarget_Violates(string kind, string withdrawal, string line, string settlement)
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {Report("w-X9", kind: "5")}
            {Settlement("w-S1", status: "0")}
            DECLARE @settlement int = SCOPE_IDENTITY();
            {Line("w-L1", "0", "NULL", "NULL")}
            DECLARE @line int = SCOPE_IDENTITY();
            {Withdrawal()}
            DECLARE @withdrawal int = SCOPE_IDENTITY();
            {Payment(kind, withdrawal, line, settlement, "0")}
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain("CK_OutgoingPayments_Target");
    }

    [Test]
    public async Task ALineMatchedToATradeSettlement_Inserts_AndWithAnAccountToo_Violates()
    {
        (await RunAsync($"""
            {World()}
            {Report("w-X9", kind: "5")}
            {Settlement("w-S1", status: "2", completed: true)}
            DECLARE @settlement int = SCOPE_IDENTITY();
            {Line("w-L1", "1", "5", "@settlement")}
            SELECT [MatchMethod] FROM [BankStatementLines] WHERE [OutsideId] = N'w-L1';
            """)).ShouldBe(5);

        var both = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {Report("w-X9", kind: "5")}
            {Settlement("w-S1", status: "2", completed: true)}
            DECLARE @settlement int = SCOPE_IDENTITY();
            {Line("w-L1", "1", "5", "@settlement", account: "@a_bank", transaction: "@a_buy")}
            """));
        both.Number.ShouldBe(547);
        both.Message.ShouldContain("CK_BankStatementLines_Match");

        var unmatched = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {Report("w-X9", kind: "5")}
            {Settlement("w-S1", status: "2", completed: true)}
            DECLARE @settlement int = SCOPE_IDENTITY();
            {Line("w-L1", "2", "NULL", "@settlement", unmatchedReason: "13")}
            """));
        unmatched.Number.ShouldBe(547);
        unmatched.Message.ShouldContain("CK_BankStatementLines_Match");
    }

    [Test]
    public async Task ALineUnmatchedAsSettlementMismatch_Inserts_AndAnItemOfKind18_Inserts()
    {
        (await RunAsync($"""
            {World()}
            {Line("w-L1", "2", "NULL", "NULL", unmatchedReason: "13")}
            INSERT INTO [BankReconciliations]
                ([PublicId], [TenantId], [ClientMoneyAccountId], [Currency], [Trigger], [RanAt], [Status], [CustodialCash],
                 [BreakCount], [BreakTotal], [TimingCount], [TimingTotal], [KnownCount], [KnownTotal], [ReservedCash],
                 [PaymentsInFlightCount], [PaymentsInFlightAmount], [Created], [CreatedBy],
                 [OpenSettlementsCount], [OpenSettlementsAmount])
            VALUES (NEWID(), @a_tenant, @cma, 'USD', 0, SYSDATETIMEOFFSET(), 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                    SYSDATETIMEOFFSET(), N'test', 2, -1001.5);
            DECLARE @run int = SCOPE_IDENTITY();
            INSERT INTO [BankReconciliationItems] ([TenantId], [ReconciliationId], [Position], [Kind], [Class], [Amount], [Count])
            VALUES (@a_tenant, @run, 1, 18, 2, 10, 1);
            SELECT COUNT(*) FROM [BankReconciliationItems] WHERE [ReconciliationId] = @run;
            """)).ShouldBe(1);
    }

    [Test]
    public async Task EfRoundTrip_KeepsATradeSettlement()
    {
        await using var db = await ScratchSchemaDatabase.CreateAsync("TradeSettlementEf");
        await db.ApplyAllAsync();
        var row = (await db.ReadAsync($"""
            {World()}
            {Report("w-X9", kind: "5")}
            SELECT @a_tenant, @cma, (SELECT [Id] FROM #report), @a_adviser;
            """)).Single();
        var (tenantId, clientMoneyId, reportId, userId) =
            (Convert.ToInt32(row[0]), Convert.ToInt32(row[1]), Convert.ToInt64(row[2]), Convert.ToInt32(row[3]));
        var at = new DateTimeOffset(2026, 11, 25, 2, 0, 4, TimeSpan.Zero);
        int settlementId;
        await using (var context = OpenDb(db.ConnectionString))
        {
            var clientMoney = await context.Set<ClientMoneyAccount>().SingleAsync(item => item.Id == clientMoneyId);
            var settlement = TradeSettlement.Create(
                clientMoney, "simulator", "0f8fad5b-S3", new DateOnly(2026, 11, 25), reportId,
                [new SettlementFill(OrderSide.Buy, 4m, 100.12m)], 2, -400.48m, clientMoney.AccountNumber, "USD", true, at);
            settlement.CreatedBy = "worker";
            context.Set<TradeSettlement>().Add(settlement);
            await context.SaveChangesAsync();
            settlementId = settlement.Id;
        }

        await using (var context = OpenDb(db.ConnectionString))
        {
            var settlement = await context.Set<TradeSettlement>().SingleAsync(item => item.Id == settlementId);
            settlement.RowVersion.ShouldNotBeEmpty();
            settlement.SetAside(userId, "Agreed with the broker", at.AddDays(1), paymentAttempted: false);
            await context.SaveChangesAsync();
        }

        await using (var context = OpenDb(db.ConnectionString))
        {
            var stored = await context.Set<TradeSettlement>().AsNoTracking().SingleAsync(item => item.Id == settlementId);
            (stored.TenantId, stored.ClientMoneyAccountId, stored.Connection, stored.BrokerSettlementId, stored.Currency)
                .ShouldBe((tenantId, clientMoneyId, "simulator", "0f8fad5b-S3", "USD"));
            (stored.SettledDate, stored.FillCount, stored.NetAmount, stored.ExecutionReportId)
                .ShouldBe((new DateOnly(2026, 11, 25), 1, -400.48m, reportId));
            (stored.BrokerFillCount, stored.BrokerNetAmount, stored.BrokerSettlementAccount.StartsWith("N-"))
                .ShouldBe((2, -400.48m, true));
            (stored.Status, stored.Failure, stored.CompletedAt)
                .ShouldBe((TradeSettlementStatus.SetAside, (TradeSettlementFailure?)null, (DateTimeOffset?)null));
            (stored.SetAsideByUserId, stored.SetAsideAt, stored.SetAsideNote)
                .ShouldBe(((int?)userId, (DateTimeOffset?)at.AddDays(1), "Agreed with the broker"));
            stored.PublicId.ShouldNotBe(Guid.Empty);
        }
    }

    private static Dictionary<string, string> Values(string change) =>
        change.Split(", ").Select(part => part.Split(": ")).ToDictionary(pair => pair[0], pair => pair[1]);

    /// <summary>A tenant (<c>@a_*</c>) with a USD client money account <c>@cma</c>; <c>#order</c> and <c>#report</c>
    /// hold the Id of the last order and report inserted.</summary>
    private static string World() => $"""
        {ScratchSchemaDatabase.TenantRows("a", Guid.NewGuid().ToString("N")[..12])}
        {ClientMoney("a", "cma")}
        CREATE TABLE #order ([Id] int);
        CREATE TABLE #report ([Id] bigint);
        """;

    private static string ClientMoney(string prefix, string variable) => $"""
        INSERT INTO [ClientMoneyAccounts]
            ([PublicId], [TenantId], [Currency], [BankName], [AccountName], [AccountNumber], [IsActive], [Created], [CreatedBy], [LastModified], [BankConnection])
        VALUES (NEWID(), @{prefix}_tenant, 'USD', N'Simulator Bank', N'Trust', N'N-{Guid.NewGuid().ToString("N")[..12]}', 1,
                SYSDATETIMEOFFSET(), N'test', SYSDATETIMEOFFSET(), N'simulator');
        DECLARE @{variable} int = SCOPE_IDENTITY();
        """;

    private static string Report(string outsideId, string kind = "0") => $"""
        INSERT INTO [ExecutionReports] ([PublicId], [Connection], [OutsideId], [Kind], [ClientOrderId], [ReceivedAt], [Status])
        VALUES (NEWID(), N'simulator', N'{outsideId}', {kind}, N'client', SYSDATETIMEOFFSET(), 0);
        IF OBJECT_ID(N'tempdb..#report') IS NOT NULL BEGIN DELETE FROM #report; INSERT INTO #report VALUES (SCOPE_IDENTITY()); END;
        """;

    /// <summary>A trade settlement of <c>@cma</c> from the report in <c>#report</c>.</summary>
    private static string Settlement(
        string settlementId,
        string status = "0",
        string failure = "NULL",
        bool completed = false,
        bool setAside = false,
        string fillCount = "2") => $"""
        INSERT INTO [TradeSettlements]
            ([PublicId], [TenantId], [ClientMoneyAccountId], [Connection], [BrokerSettlementId], [Currency], [SettledDate],
             [FillCount], [NetAmount], [BrokerFillCount], [BrokerNetAmount], [BrokerSettlementAccount], [ExecutionReportId],
             [Status], [Failure], [CompletedAt], [SetAsideByUserId], [SetAsideAt], [SetAsideNote], [Created], [CreatedBy])
        SELECT NEWID(), @a_tenant, @cma, N'simulator', N'{settlementId}', 'USD', '2026-11-25',
               {fillCount}, -1001.5, 2, -1001.5, N'SIM-DEMO-USD', r.[Id],
               {status}, {failure}, {(completed ? "SYSDATETIMEOFFSET()" : "NULL")},
               {(setAside ? "@a_adviser" : "NULL")}, {(setAside ? "SYSDATETIMEOFFSET()" : "NULL")},
               {(setAside ? "N'Agreed'" : "NULL")}, SYSDATETIMEOFFSET(), N'test'
        FROM #report r;
        """;

    private static string OrderRow(
        string status = "0",
        string filled = "0",
        string clientMoney = "NULL",
        string accountNumber = "NULL",
        bool settlementColumns = true) => $"""
        INSERT INTO [Orders]
            ([PublicId], [TenantId], [AccountId], [InstrumentId], [Side], [Type], [TimeInForce], [Quantity], [Currency],
             [ReferencePrice], [ReferencePriceDate], [PriceLimit], [Connection], [Status], [FilledQuantity], [PostedGross],
             [FeeCharged], [ReservedCash], [ReservedQuantity], [PlacedByUserId], [PlacedAt], [Attempts], [Created], [CreatedBy]
             {(settlementColumns ? ", [ClientMoneyAccountId], [SettlementAccountNumber]" : "")})
        VALUES (NEWID(), @a_tenant, @a_brokerage, @a_instrument, 0, 0, 0, 10, 'USD',
                100, '2026-11-23', 105, N'simulator', {status}, {filled}, 0,
                0, 0, 0, @a_adviser, SYSDATETIMEOFFSET(), 0, SYSDATETIMEOFFSET(), N'test'
                {(settlementColumns ? $", {clientMoney}, {accountNumber}" : "")});
        DELETE FROM #order; INSERT INTO #order VALUES (SCOPE_IDENTITY());
        """;

    /// <summary>A fill of the order in <c>#order</c> from the report in <c>#report</c>; a broken fill is broken by
    /// <paramref name="breakReport"/>, by default the first <c>Broken</c> report.</summary>
    private static string Fill(
        string status,
        string transaction,
        string outcome,
        string reversal,
        string holdReason,
        bool resolved = false,
        string? breakReport = null)
    {
        var posted = transaction != "NULL";
        var broken = outcome != "NULL" || status == "2";
        var report = breakReport ?? "(SELECT TOP 1 [Id] FROM [ExecutionReports] WHERE [Kind] = 4 ORDER BY [Id] DESC)";
        return $"""
            INSERT INTO [OrderFills]
                ([PublicId], [TenantId], [OrderId], [ExecutionReportId], [Quantity], [Price], [TradeDate], [SettlementDate],
                 [Status], [TransactionId], [NetCash], [PostedAt], [BrokenAt], [BreakReportId], [BreakReason], [BreakOutcome],
                 [BreakHoldReason], [ReversalTransactionId], [BreakResolvedByUserId], [BreakResolvedAt], [BreakNote])
            SELECT NEWID(), @a_tenant, o.[Id], r.[Id], 4, 100.12, '2026-11-23', '2026-11-25',
                   {status}, {transaction}, {(posted ? "-405.48" : "NULL")}, {(posted ? "SYSDATETIMEOFFSET()" : "NULL")},
                   {(broken && outcome != "NULL" ? "SYSDATETIMEOFFSET()" : "NULL")}, {(broken && outcome != "NULL" ? report : "NULL")},
                   {(broken ? "N'Broken by the exchange'" : "NULL")}, {outcome},
                   {holdReason}, {reversal}, {(resolved ? "@a_adviser" : "NULL")}, {(resolved ? "SYSDATETIMEOFFSET()" : "NULL")},
                   {(resolved ? "N'Corrected'" : "NULL")}
            FROM #order o CROSS JOIN #report r;
            """;
    }

    private static string Withdrawal() => """
        INSERT INTO [NominatedBankAccounts]
            ([PublicId], [TenantId], [CustomerId], [Currency], [BankName], [AccountName], [AccountNumber], [AccountNumberKey], [Status],
             [AddedByUserId], [AddedAt], [Created], [CreatedBy])
        VALUES (NEWID(), @a_tenant, @a_customer, 'USD', N'ANZ', N'J Smith', N'0601', N'0601', 0, @a_adviser, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'test');
        INSERT INTO [Withdrawals]
            ([PublicId], [TenantId], [AccountId], [NominatedBankAccountId], [Amount], [Currency], [Reference], [Status],
             [RequestedByUserId], [RequestedAt], [Created], [CreatedBy])
        VALUES (NEWID(), @a_tenant, @a_bank, SCOPE_IDENTITY(), 300, 'USD', N'K7M3Q9PXB', 0, @a_adviser, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'test');
        """;

    private static string Payment(string kind, string withdrawal, string line, string settlement, string status) => $"""
        INSERT INTO [OutgoingPayments]
            ([PublicId], [TenantId], [ClientMoneyAccountId], [Connection], [Kind], [InstructionPublicId], [WithdrawalId], [ReturnOfLineId],
             [TradeSettlementId], [Amount], [Currency], [Status], [RejectReason], [Created], [CreatedBy])
        VALUES (NEWID(), @a_tenant, @cma, N'simulator', {kind}, NEWID(), {withdrawal}, {line}, {settlement}, 1001.5, 'USD', {status},
                {(status == "2" ? "0" : "NULL")}, SYSDATETIMEOFFSET(), N'test');
        """;

    private static string Line(
        string outsideId,
        string status,
        string method,
        string settlement,
        string unmatchedReason = "NULL",
        string account = "NULL",
        string transaction = "NULL") => $"""
        INSERT INTO [BankStatementLines]
            ([PublicId], [TenantId], [ClientMoneyAccountId], [Connection], [OutsideId], [ValueDate], [Direction], [Amount], [Currency],
             [ReceivedAt], [Status], [UnmatchedReason], [MatchMethod], [MatchedAt], [MatchedAccountId], [MatchedTransactionId],
             [TradeSettlementId], [Created], [CreatedBy])
        VALUES (NEWID(), @a_tenant, @cma, N'simulator', N'{outsideId}', '2026-11-25', 1, 1001.5, 'USD', SYSDATETIMEOFFSET(), {status},
                {unmatchedReason}, {method}, {(method == "NULL" ? "NULL" : "SYSDATETIMEOFFSET()")}, {account}, {transaction},
                {settlement}, SYSDATETIMEOFFSET(), N'test');
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
