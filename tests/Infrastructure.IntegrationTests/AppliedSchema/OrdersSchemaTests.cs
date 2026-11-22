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

/// <summary>Orders §6.1: the broker connection of a Tenant, orders, fills, execution reports, the broker feed state,
/// the execution detail and the widened source checks (script 0051).</summary>
public class OrdersSchemaTests
{
    private const string Script = "0051_orders.sql";

    [Test]
    public void SchemaDirectory_Includes0051Script()
    {
        new FileSchemaScriptSource(ScratchSchemaDatabase.SchemaDirectory()).GetScripts()
            .Select(script => script.Name)
            .ShouldContain(Script);
    }

    [Test]
    public async Task Apply0051_SetsSimulatorOnEveryExistingTenant_AndANewTenantHasNone()
    {
        await using var db = await ScratchSchemaDatabase.CreateAsync("Orders0051");
        await db.ApplyBeforeAsync(Script);
        await db.ExecuteAsync($"{Tenant("before-a")}{Tenant("before-b")}");

        await db.ApplyAllAsync();
        await db.ExecuteAsync(Tenant("after"));

        var column = await db.ReadAsync("""
            SELECT c.[max_length], c.[is_nullable], c.[default_object_id]
            FROM sys.columns c
            WHERE c.[object_id] = OBJECT_ID(N'Tenants') AND c.[name] = N'BrokerConnection';
            """);
        var shape = column.ShouldHaveSingleItem();
        ((short)shape[0]).ShouldBe((short)100);
        ((bool)shape[1]).ShouldBeTrue();
        ((int)shape[2]).ShouldBe(0);
        var connections = await db.ReadAsync(
            "SELECT [Code], [BrokerConnection] FROM [Tenants] WHERE [Code] IN (N'before-a', N'before-b', N'after') ORDER BY [Id];");
        connections.Select(row => ((string)row[0], row[1] as string))
            .ShouldBe([("before-a", "simulator"), ("before-b", "simulator"), ("after", null)]);
    }

    [Test]
    public async Task AReport_Inserts_AndASecondWithTheSameConnectionAndOutsideId_Violates()
    {
        (await RunAsync($"""
            {Report("w-X1")}
            {Report("w-X1", connection: "other")}
            SELECT COUNT(*) FROM [ExecutionReports] WHERE [OutsideId] = N'w-X1';
            """)).ShouldBe(2);

        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {Report("w-X1")}
            {Report("w-X1")}
            """));
        exception.Number.ShouldBe(2601);
        exception.Message.ShouldContain("UX_ExecutionReports_Connection_OutsideId");
    }

    [Test]
    public async Task AReportWithNoKind_Inserts()
    {
        (await RunAsync($"""
            {Report("w-X2", kind: "NULL")}
            SELECT COUNT(*) FROM [ExecutionReports] WHERE [OutsideId] = N'w-X2' AND [Kind] IS NULL;
            """)).ShouldBe(1);
    }

    [TestCase("kind: 4", "CK_ExecutionReports_Kind")]
    [TestCase("status: 3", "CK_ExecutionReports_Status")]
    [TestCase("status: 2", "CK_ExecutionReports_Skipped")]
    [TestCase("skipReason: 1", "CK_ExecutionReports_Skipped")]
    [TestCase("status: 2, skipReason: 5", "CK_ExecutionReports_SkipReason")]
    public async Task AReportOutsideTheChecks_Violates(string change, string constraint)
    {
        var values = Values(change);
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync(Report(
            "w-X3",
            kind: values.GetValueOrDefault("kind", "0"),
            status: values.GetValueOrDefault("status", "0"),
            skipReason: values.GetValueOrDefault("skipReason", "NULL"))));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain(constraint);
    }

    [Test]
    public async Task AnOrder_InEveryStatus_Inserts()
    {
        (await RunAsync($"""
            {World()}
            {OrderRow(status: "0")}
            {OrderRow(status: "1")}
            {OrderRow(status: "2", filled: "4")}
            {OrderRow(status: "3", filled: "10", closed: true)}
            {OrderRow(status: "4", closed: true)}
            {OrderRow(status: "5", rejectReason: "2", closed: true)}
            {OrderRow(status: "6", closed: true)}
            SELECT COUNT(*) FROM [Orders] WHERE [TenantId] = @a_tenant;
            """)).ShouldBe(7);
    }

    [TestCase("side: 2", "CK_Orders_Side")]
    [TestCase("type: 1", "CK_Orders_Type")]
    [TestCase("timeInForce: 1", "CK_Orders_TimeInForce")]
    [TestCase("quantity: 0", "CK_Orders_Quantity")]
    [TestCase("filled: 10.00000001", "CK_Orders_FilledQuantity")]
    [TestCase("filled: -1", "CK_Orders_FilledQuantity")]
    [TestCase("referencePrice: 0", "CK_Orders_ReferencePrice")]
    [TestCase("priceLimit: 0", "CK_Orders_PriceLimit")]
    [TestCase("status: 7", "CK_Orders_Status")]
    [TestCase("reservedCash: -0.01", "CK_Orders_ReservedCash")]
    [TestCase("reservedQuantity: -1", "CK_Orders_ReservedQuantity")]
    [TestCase("lastFailure: 5", "CK_Orders_LastFailure")]
    [TestCase("status: 5, rejectReason: 4, closed: 1", "CK_Orders_RejectReason")]
    [TestCase("status: 5, closed: 1", "CK_Orders_Rejected")]
    [TestCase("rejectReason: 0", "CK_Orders_Rejected")]
    [TestCase("status: 3, filled: 10", "CK_Orders_ClosedAt")]
    [TestCase("status: 4", "CK_Orders_ClosedAt")]
    [TestCase("status: 1, closed: 1", "CK_Orders_ClosedAt")]
    public async Task AnOrderOutsideTheChecks_Violates(string change, string constraint)
    {
        var values = Values(change);
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {OrderRow(
                side: values.GetValueOrDefault("side", "0"),
                type: values.GetValueOrDefault("type", "0"),
                timeInForce: values.GetValueOrDefault("timeInForce", "0"),
                quantity: values.GetValueOrDefault("quantity", "10"),
                filled: values.GetValueOrDefault("filled", "0"),
                referencePrice: values.GetValueOrDefault("referencePrice", "100"),
                priceLimit: values.GetValueOrDefault("priceLimit", "105"),
                status: values.GetValueOrDefault("status", "0"),
                reservedCash: values.GetValueOrDefault("reservedCash", "0"),
                reservedQuantity: values.GetValueOrDefault("reservedQuantity", "0"),
                lastFailure: values.GetValueOrDefault("lastFailure", "NULL"),
                rejectReason: values.GetValueOrDefault("rejectReason", "NULL"),
                closed: values.GetValueOrDefault("closed", "0") == "1")}
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain(constraint);
    }

    [Test]
    public async Task AnOrderOfAnotherTenantsAccount_Violates()
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {ScratchSchemaDatabase.TenantRows("b", Guid.NewGuid().ToString("N")[..12])}
            {OrderRow(account: "@b_brokerage")}
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain("FK_Orders_Accounts_TenantId_AccountId");
    }

    [Test]
    public async Task APendingFill_AndAPostedSettledFill_Insert_AndASecondFillForTheSameReport_Violates()
    {
        (await RunAsync($"""
            {World()}
            {OrderRow(status: "2", filled: "4")}
            {Report("w-X1")}
            {Fill(holdReason: "0")}
            {Report("w-X2")}
            {Fill(status: "1", posted: true, settled: true)}
            SELECT COUNT(*) FROM [OrderFills] WHERE [OrderId] = (SELECT [Id] FROM #order);
            """)).ShouldBe(2);

        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {OrderRow(status: "2", filled: "4")}
            {Report("w-X1")}
            {Fill()}
            {Fill()}
            """));
        exception.Number.ShouldBe(2627);
        exception.Message.ShouldContain("UQ_OrderFills_ExecutionReportId");
    }

    [TestCase("quantity: 0", "CK_OrderFills_Quantity")]
    [TestCase("price: 0", "CK_OrderFills_Price")]
    [TestCase("status: 2", "CK_OrderFills_Status")]
    [TestCase("holdReason: 4", "CK_OrderFills_HoldReason")]
    [TestCase("status: 1, posted: 1, holdReason: 0", "CK_OrderFills_Held")]
    [TestCase("status: 1", "CK_OrderFills_Posted")]
    [TestCase("posted: 1", "CK_OrderFills_Posted")]
    [TestCase("settledAt: 1", "CK_OrderFills_Settled")]
    [TestCase("settledDate: 1", "CK_OrderFills_Settled")]
    public async Task AFillOutsideTheChecks_Violates(string change, string constraint)
    {
        var values = Values(change);
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {OrderRow(status: "2", filled: "4")}
            {Report("w-X1")}
            {Fill(
                quantity: values.GetValueOrDefault("quantity", "4"),
                price: values.GetValueOrDefault("price", "100.12"),
                status: values.GetValueOrDefault("status", "0"),
                holdReason: values.GetValueOrDefault("holdReason", "NULL"),
                posted: values.GetValueOrDefault("posted", "0") == "1",
                settledAt: values.GetValueOrDefault("settledAt", "0") == "1",
                settledDate: values.GetValueOrDefault("settledDate", "0") == "1")}
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain(constraint);
    }

    [Test]
    public async Task SourceKind8_NeedsASourcePublicId_And9Violates()
    {
        (await RunAsync($"""
            {World()}
            {InsertTransaction(8, "NEWID()")}
            SELECT [SourceKind] FROM [Transactions] WHERE [Id] = SCOPE_IDENTITY();
            """)).ShouldBe(8);

        var noId = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertTransaction(8, "NULL")}
            """));
        noId.Number.ShouldBe(547);
        noId.Message.ShouldContain("CK_Transactions_SourcePublicId");

        var nine = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertTransaction(9, "NEWID()")}
            """));
        nine.Number.ShouldBe(547);
        nine.Message.ShouldContain("CK_Transactions_SourceKind");
    }

    [Test]
    public async Task AnExecutionDetail_IsOnePerTransaction()
    {
        (await RunAsync($"""
            {World()}
            {Detail("@a_buy")}
            SELECT [Price] FROM [TransactionExecutionDetails] WHERE [TransactionId] = @a_buy;
            """)).ShouldBe(100.12m);

        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {Detail("@a_buy")}
            {Detail("@a_buy")}
            """));
        exception.Number.ShouldBe(2627);
        exception.Message.ShouldContain("PK_TransactionExecutionDetails");
    }

    [TestCase("0", "2026-11-25", "CK_TransactionExecutionDetails_Price")]
    [TestCase("100.12", "2026-11-22", "CK_TransactionExecutionDetails_Dates")]
    public async Task AnExecutionDetailOutsideTheChecks_Violates(string price, string settlementDate, string constraint)
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {Detail("@a_buy", price, settlementDate)}
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain(constraint);
    }

    [Test]
    public async Task ABrokerFeed_IsOnePerConnection_AndInvalidItemIsItsLastFailure()
    {
        (await RunAsync("""
            INSERT INTO [BrokerFeeds] ([Connection], [Cursor], [LastFailure]) VALUES (N'simulator', N'w:3', 5);
            SELECT [LastFailure] FROM [BrokerFeeds] WHERE [Connection] = N'simulator';
            """)).ShouldBe(5);

        var twice = await Should.ThrowAsync<SqlException>(() => RunAsync("""
            INSERT INTO [BrokerFeeds] ([Connection]) VALUES (N'simulator');
            INSERT INTO [BrokerFeeds] ([Connection]) VALUES (N'simulator');
            """));
        twice.Number.ShouldBe(2627);
        twice.Message.ShouldContain("PK_BrokerFeeds");

        var failure = await Should.ThrowAsync<SqlException>(() => RunAsync(
            "INSERT INTO [BrokerFeeds] ([Connection], [LastFailure]) VALUES (N'simulator', 6);"));
        failure.Number.ShouldBe(547);
        failure.Message.ShouldContain("CK_BrokerFeeds_LastFailure");
    }

    [Test]
    public async Task EfRoundTrip_KeepsAnOrderAndItsFills()
    {
        await using var db = await ScratchSchemaDatabase.CreateAsync("OrderEf");
        await db.ApplyAllAsync();
        var row = (await db.ReadAsync($"""
            {ScratchSchemaDatabase.TenantRows("a", Guid.NewGuid().ToString("N")[..12])}
            SELECT @a_tenant, @a_instrument, @a_brokerage, @a_buy, @a_adviser;
            """)).Single();
        var (tenantId, instrumentId, accountId, transactionId, userId) = (
            Convert.ToInt32(row[0]), Convert.ToInt32(row[1]), Convert.ToInt32(row[2]), Convert.ToInt32(row[3]),
            Convert.ToInt32(row[4]));
        int scheduleId;
        await using (var context = OpenDb(db.ConnectionString))
        {
            // 0.5 % with a minimum of 10.00: the order of 10 at a limit of 105 reserves 1050 + 10.
            var usd = await context.Set<Currency>().SingleAsync(currency => currency.Code == "USD");
            var schedule = TradeFeeSchedule.Create(
                tenantId, usd, new DateOnly(2026, 11, 1), 10m, null, [new MyWealthV2.Domain.ValueObjects.FeeBand(0m, 0.5m)],
                new DateOnly(2026, 10, 15));
            schedule.CreatedBy = "test";
            context.Set<TradeFeeSchedule>().Add(schedule);
            await context.SaveChangesAsync();
            scheduleId = schedule.Id;
        }

        var at = new DateTimeOffset(2026, 11, 23, 2, 10, 0, TimeSpan.Zero);
        var day = new DateOnly(2026, 11, 23);
        var report = ExecutionReport.Receive(
            "simulator", "w-X1", ExecutionReportKind.Fill, "client", "w-O1", 4m, 100.12m, day, day.AddDays(2), "CN-1", null, null,
            null, at.AddMinutes(1));
        int orderId;
        await using (var context = OpenDb(db.ConnectionString))
        {
            var fees = await FeesAsync(context, scheduleId);
            var order = Order.Place(
                tenantId, accountId, instrumentId, OrderSide.Buy, 10m, "USD", 100m, day, fees, "simulator", "Monthly top-up",
                userId, at);
            order.CreatedBy = "test";
            order.ReservedCash.ShouldBe(1060m);
            context.Set<Order>().Add(order);
            context.Set<ExecutionReport>().Add(report);
            await context.SaveChangesAsync();
            orderId = order.Id;
        }

        await using (var context = OpenDb(db.ConnectionString))
        {
            var fees = await FeesAsync(context, scheduleId);
            var order = await context.Set<Order>().Include(item => item.Fills).SingleAsync(item => item.Id == orderId);
            order.RowVersion.ShouldNotBeEmpty();
            order.RecordAttempt(at.AddSeconds(30));
            order.RecordFailure(BrokerFailure.Timeout);
            order.RecordAttempt(at.AddMinutes(1));
            order.Accept("w-O1", at.AddMinutes(1));
            order.RequestCancel(userId, "Wrong quantity", at.AddMinutes(2));
            var fill = order.AddFill(report.Id, 4m, 100.12m, day, day.AddDays(2), "CN-1", at.AddMinutes(3), fees);
            await context.SaveChangesAsync();

            fill.MarkPosted(transactionId, -410.48m, at.AddMinutes(3));
            order.RecordPostedFill(400.48m, 10m, fees);
            fill.Settle(day.AddDays(2), at.AddDays(2));
            order.End(OrderStatus.Cancelled, "Requested", at.AddMinutes(4), fees);
            await context.SaveChangesAsync();
        }

        await using (var context = OpenDb(db.ConnectionString))
        {
            var order = await context.Set<Order>().AsNoTracking().Include(item => item.Fills).SingleAsync(item => item.Id == orderId);
            (order.TenantId, order.AccountId, order.InstrumentId, order.Side, order.Type, order.TimeInForce)
                .ShouldBe((tenantId, accountId, instrumentId, OrderSide.Buy, OrderType.Market, TimeInForce.Day));
            (order.Quantity, order.Currency, order.ReferencePrice, order.ReferencePriceDate, order.PriceLimit)
                .ShouldBe((10m, "USD", 100m, day, 105m));
            (order.TradeFeeScheduleId, order.Connection, order.Memo, order.PlacedByUserId, order.PlacedAt)
                .ShouldBe(((int?)scheduleId, "simulator", "Monthly top-up", userId, at));
            (order.Status, order.FilledQuantity, order.PostedGross, order.FeeCharged, order.ReservedCash, order.ReservedQuantity)
                .ShouldBe((OrderStatus.Cancelled, 4m, 400.48m, 10m, 0m, 0m));
            (order.Attempts, order.LastAttemptAt, order.LastFailure, order.AcceptedAt, order.BrokerOrderId)
                .ShouldBe((2, (DateTimeOffset?)at.AddMinutes(1), (BrokerFailure?)null, (DateTimeOffset?)at.AddMinutes(1), "w-O1"));
            (order.CancelRequestedAt, order.CancelRequestedByUserId, order.CancelNote, order.CancelAnsweredAt)
                .ShouldBe(((DateTimeOffset?)at.AddMinutes(2), (int?)userId, "Wrong quantity", (DateTimeOffset?)null));
            (order.ClosedAt, order.CloseReason, order.RejectReason)
                .ShouldBe(((DateTimeOffset?)at.AddMinutes(4), "Requested", (OrderRejectReason?)null));

            var fill = order.Fills.ShouldHaveSingleItem();
            (fill.TenantId, fill.OrderId, fill.ExecutionReportId, fill.Quantity, fill.Price, fill.TradeDate, fill.SettlementDate)
                .ShouldBe((tenantId, orderId, report.Id, 4m, 100.12m, day, day.AddDays(2)));
            (fill.BrokerReference, fill.Status, fill.HoldReason, fill.TransactionId, fill.NetCash, fill.PostedAt)
                .ShouldBe(("CN-1", OrderFillStatus.Posted, (FillHoldReason?)null, (int?)transactionId, (decimal?)-410.48m,
                    (DateTimeOffset?)at.AddMinutes(3)));
            (fill.SettledAt, fill.SettledDate).ShouldBe(((DateTimeOffset?)at.AddDays(2), (DateOnly?)day.AddDays(2)));
            fill.PublicId.ShouldNotBe(Guid.Empty);
        }
    }

    [Test]
    public async Task EfRoundTrip_KeepsAnExecutionReportAndTheFeedCursor()
    {
        await using var db = await ScratchSchemaDatabase.CreateAsync("ExecutionReportEf");
        await db.ApplyAllAsync();
        var at = new DateTimeOffset(2026, 11, 23, 2, 11, 2, TimeSpan.Zero);
        var day = new DateOnly(2026, 11, 23);
        var settled = ExecutionReport.Receive(
            "simulator", "w-X3", ExecutionReportKind.Settled, "client", "w-O1", null, null, null, null, null, "w-X1",
            day.AddDays(2), null, at);
        var unknown = ExecutionReport.Receive(
            "simulator", "w-X4", null, "client", null, null, null, null, null, null, null, null, "Something new", at);
        var feed = BrokerFeed.Start("simulator");
        feed.Advance("w:4", at);
        feed.RecordFailure(BrokerFailure.InvalidItem, at.AddMinutes(1));
        await using (var context = OpenDb(db.ConnectionString))
        {
            context.Set<ExecutionReport>().AddRange(settled, unknown);
            context.Set<BrokerFeed>().Add(feed);
            await context.SaveChangesAsync();
        }

        await using (var context = OpenDb(db.ConnectionString))
        {
            var stored = await context.Set<ExecutionReport>().SingleAsync(item => item.Id == unknown.Id);
            stored.Skip(ExecutionReportSkipReason.UnsupportedKind, at.AddSeconds(1));
            await context.SaveChangesAsync();
        }

        await using (var context = OpenDb(db.ConnectionString))
        {
            var first = await context.Set<ExecutionReport>().AsNoTracking().SingleAsync(item => item.Id == settled.Id);
            (first.PublicId, first.Connection, first.OutsideId, first.Kind, first.ClientOrderId, first.BrokerOrderId)
                .ShouldBe((settled.PublicId, "simulator", "w-X3", (ExecutionReportKind?)ExecutionReportKind.Settled, "client", "w-O1"));
            (first.FillOutsideId, first.SettledDate, first.Status, first.ReceivedAt)
                .ShouldBe(("w-X1", (DateOnly?)day.AddDays(2), ExecutionReportStatus.Received, at));

            var second = await context.Set<ExecutionReport>().AsNoTracking().SingleAsync(item => item.Id == unknown.Id);
            (second.Kind, second.Reason, second.Status, second.SkipReason, second.ProcessedAt).ShouldBe((
                (ExecutionReportKind?)null, "Something new", ExecutionReportStatus.Skipped,
                (ExecutionReportSkipReason?)ExecutionReportSkipReason.UnsupportedKind, (DateTimeOffset?)at.AddSeconds(1)));

            var storedFeed = await context.Set<BrokerFeed>().AsNoTracking().SingleAsync(item => item.Connection == "simulator");
            (storedFeed.Cursor, storedFeed.LastSucceededAt, storedFeed.LastFailedAt, storedFeed.LastFailure).ShouldBe((
                "w:4", (DateTimeOffset?)at, (DateTimeOffset?)at.AddMinutes(1), (BrokerFailure?)BrokerFailure.InvalidItem));
        }
    }

    [Test]
    public async Task EfRoundTrip_KeepsAFillsTransactionAndItsExecutionDetail()
    {
        await using var db = await ScratchSchemaDatabase.CreateAsync("OrderFillPostingEf");
        await db.ApplyAllAsync();
        var row = (await db.ReadAsync($"""
            {ScratchSchemaDatabase.TenantRows("a", Guid.NewGuid().ToString("N")[..12])}
            SELECT @a_tenant, @a_instrument, @a_bank;
            """)).Single();
        var (tenantId, instrumentId, accountId) = (Convert.ToInt32(row[0]), Convert.ToInt32(row[1]), Convert.ToInt32(row[2]));
        var orderId = Guid.NewGuid();
        var fillId = Guid.NewGuid();
        var day = new DateOnly(2026, 11, 23);
        var posting = Transaction.PostBuy(
            tenantId, accountId, AccountType.Brokerage, "USD", instrumentId, 4m,
            MyWealthV2.Domain.ValueObjects.Money.Create(100.12m, "USD"), 1m, new DateTimeOffset(2026, 11, 23, 2, 11, 2, TimeSpan.Zero),
            Transaction.BuyOrderMemo, null, MyWealthV2.Domain.ValueObjects.TransactionSource.OrderFill(orderId), currentCashSum: 5000m,
            execution: new MyWealthV2.Domain.ValueObjects.TradeExecution(fillId, day, day.AddDays(2), "CN-1"));
        posting.CreatedBy = "worker";
        posting.CashLegs.ForEach(leg => leg.CreatedBy = "worker");
        posting.SecurityLegs.ForEach(leg => leg.CreatedBy = "worker");

        await using (var context = OpenDb(db.ConnectionString))
        {
            context.Set<Transaction>().Add(posting);
            await context.SaveChangesAsync();
        }

        await using (var context = OpenDb(db.ConnectionString))
        {
            var stored = await context.Set<Transaction>().AsNoTracking()
                .Include(transaction => transaction.ExecutionDetail)
                .SingleAsync(transaction => transaction.Id == posting.Id);
            (stored.Type, stored.SourceKind, stored.SourcePublicId, stored.Memo)
                .ShouldBe((TransactionType.Buy, TransactionSourceKind.OrderFill, (Guid?)orderId, "Buy order"));
            var detail = stored.ExecutionDetail.ShouldNotBeNull();
            (detail.TenantId, detail.FillPublicId, detail.Price, detail.TradeDate, detail.SettlementDate, detail.BrokerReference)
                .ShouldBe((tenantId, fillId, 100.12m, day, day.AddDays(2), "CN-1"));
        }
    }

    private static async Task<OrderFeeTerms> FeesAsync(ApplicationDbContext context, int scheduleId) =>
        new(await context.Set<TradeFeeSchedule>().Include(schedule => schedule.BandRows).SingleAsync(schedule => schedule.Id == scheduleId), 2);

    private static Dictionary<string, string> Values(string change) =>
        change.Split(", ").Select(part => part.Split(": ")).ToDictionary(pair => pair[0], pair => pair[1]);

    private static string Tenant(string code) => $"""
        INSERT INTO [Tenants] ([PublicId], [Name], [Code], [Created], [LastModified], [ReportingCurrency])
        VALUES (NEWID(), N'Tenant {code}', N'{code}', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), 'NZD');

        """;

    /// <summary>A tenant (<c>@a_*</c> from <see cref="ScratchSchemaDatabase.TenantRows"/>); <c>#order</c> and
    /// <c>#report</c> hold the Id of the last order and report inserted.</summary>
    private static string World() => $"""
        {ScratchSchemaDatabase.TenantRows("a", Guid.NewGuid().ToString("N")[..12])}
        CREATE TABLE #order ([Id] int);
        CREATE TABLE #report ([Id] bigint);
        """;

    private static string Report(
        string outsideId,
        string connection = "simulator",
        string kind = "0",
        string status = "0",
        string skipReason = "NULL") => $"""
        INSERT INTO [ExecutionReports] ([PublicId], [Connection], [OutsideId], [Kind], [ClientOrderId], [ReceivedAt], [Status], [SkipReason])
        VALUES (NEWID(), N'{connection}', N'{outsideId}', {kind}, N'client', SYSDATETIMEOFFSET(), {status}, {skipReason});
        IF OBJECT_ID(N'tempdb..#report') IS NOT NULL BEGIN DELETE FROM #report; INSERT INTO #report VALUES (SCOPE_IDENTITY()); END;
        """;

    /// <summary>An order on <c>@a_brokerage</c> in <c>@a_instrument</c>; its Id is the one row of <c>#order</c>.</summary>
    private static string OrderRow(
        string account = "@a_brokerage",
        string side = "0",
        string type = "0",
        string timeInForce = "0",
        string quantity = "10",
        string filled = "0",
        string referencePrice = "100",
        string priceLimit = "105",
        string status = "0",
        string reservedCash = "0",
        string reservedQuantity = "0",
        string lastFailure = "NULL",
        string rejectReason = "NULL",
        bool closed = false) => $"""
        INSERT INTO [Orders]
            ([PublicId], [TenantId], [AccountId], [InstrumentId], [Side], [Type], [TimeInForce], [Quantity], [Currency],
             [ReferencePrice], [ReferencePriceDate], [PriceLimit], [Connection], [Status], [FilledQuantity], [PostedGross],
             [FeeCharged], [ReservedCash], [ReservedQuantity], [PlacedByUserId], [PlacedAt], [Attempts], [LastFailure],
             [RejectReason], [ClosedAt], [Created], [CreatedBy])
        VALUES (NEWID(), @a_tenant, {account}, @a_instrument, {side}, {type}, {timeInForce}, {quantity}, 'USD',
                {referencePrice}, '2026-11-23', {priceLimit}, N'simulator', {status}, {filled}, 0,
                0, {reservedCash}, {reservedQuantity}, @a_adviser, SYSDATETIMEOFFSET(), 0, {lastFailure},
                {rejectReason}, {(closed ? "SYSDATETIMEOFFSET()" : "NULL")}, SYSDATETIMEOFFSET(), N'test');
        DELETE FROM #order; INSERT INTO #order VALUES (SCOPE_IDENTITY());
        """;

    /// <summary>A fill of the order in <c>#order</c> from the report in <c>#report</c>.</summary>
    private static string Fill(
        string quantity = "4",
        string price = "100.12",
        string status = "0",
        string holdReason = "NULL",
        bool posted = false,
        bool settled = false,
        bool settledAt = false,
        bool settledDate = false) => $"""
        INSERT INTO [OrderFills]
            ([PublicId], [TenantId], [OrderId], [ExecutionReportId], [Quantity], [Price], [TradeDate], [SettlementDate],
             [Status], [HoldReason], [TransactionId], [NetCash], [PostedAt], [SettledAt], [SettledDate])
        SELECT NEWID(), @a_tenant, o.[Id], r.[Id], {quantity}, {price}, '2026-11-23', '2026-11-25',
               {status}, {holdReason}, {(posted ? "@a_buy" : "NULL")}, {(posted ? "-405.48" : "NULL")},
               {(posted ? "SYSDATETIMEOFFSET()" : "NULL")}, {(settled || settledAt ? "SYSDATETIMEOFFSET()" : "NULL")},
               {(settled || settledDate ? "'2026-11-25'" : "NULL")}
        FROM #order o CROSS JOIN #report r;
        """;

    private static string Detail(string transaction, string price = "100.12", string settlementDate = "2026-11-25") => $"""
        INSERT INTO [TransactionExecutionDetails]
            ([TransactionId], [TenantId], [FillPublicId], [Price], [TradeDate], [SettlementDate], [BrokerReference])
        VALUES ({transaction}, @a_tenant, NEWID(), {price}, '2026-11-23', '{settlementDate}', N'CN-1');
        """;

    private static string InsertTransaction(int sourceKind, string sourcePublicId) => $"""
        INSERT INTO [Transactions] ([PublicId], [TenantId], [Type], [SourceKind], [SourcePublicId], [BookedAt], [Created], [CreatedBy])
        VALUES (NEWID(), @a_tenant, 7, {sourceKind}, {sourcePublicId}, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'test');
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
