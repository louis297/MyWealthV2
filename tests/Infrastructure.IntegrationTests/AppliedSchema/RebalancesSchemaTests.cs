using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Orders;
using MyWealthV2.Domain.Rebalances;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Schema;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

/// <summary>Rebalances §6: <c>Rebalances</c>, <c>RebalanceTargets</c>, <c>RebalanceLines</c> and the rebalance of an
/// order (script 0053).</summary>
[Parallelizable]
public class RebalancesSchemaTests
{
    private const string Script = "0053_rebalances.sql";

    [Test]
    public void SchemaDirectory_Includes0053Script()
    {
        new FileSchemaScriptSource(ScratchSchemaDatabase.SchemaDirectory()).GetScripts()
            .Select(script => script.Name)
            .ShouldContain(Script);
    }

    [Test]
    public async Task Apply0053_KeepsExistingOrders_WithNoRebalance()
    {
        await using var db = await ScratchSchemaDatabase.CreateAsync("Rebalances0053");
        await db.ApplyBeforeAsync(Script);
        await db.ExecuteAsync($"""
            {World(withAssignment: false)}
            {OrderRow(rebalance: null)}
            """);

        await db.ApplyAllAsync();

        var order = (await db.ReadAsync("SELECT [RebalanceId] FROM [Orders];")).ShouldHaveSingleItem();
        (order[0] is DBNull).ShouldBeTrue();
    }

    [Test]
    public async Task Apply0053_DropsTheThreeLastRebalanceColumnsOfTheAssignment()
    {
        var columns = await ScratchSchemaDatabase.ReadAsync(SchemaDatabaseSetup.ConnectionString, """
            SELECT [name] FROM sys.columns WHERE [object_id] = OBJECT_ID(N'AccountPortfolioAssignments');
            """);

        columns.Select(row => (string)row[0]).ShouldNotContain(name => name.StartsWith("LastRebalance"));
        (await ScratchSchemaDatabase.ReadAsync(SchemaDatabaseSetup.ConnectionString,
                "SELECT [name] FROM sys.check_constraints WHERE [name] = N'CK_AccountPortfolioAssignments_LastRebalanceStatus';"))
            .ShouldBeEmpty();
    }

    [TestCase("0", false, false, false, false)]
    [TestCase("1", true, false, false, false)]
    [TestCase("2", true, false, false, false)]
    [TestCase("3", true, true, false, false)]
    [TestCase("4", true, true, true, false)]
    [TestCase("5", false, true, false, true)]
    [TestCase("6", false, true, false, false)]
    [TestCase("7", false, true, true, false)]
    public async Task ARebalance_InEveryStatus_WithItsColumns_Inserts(
        string status, bool confirmed, bool ended, bool failure, bool cancelled)
    {
        (await RunAsync($"""
            {World()}
            {RebalanceRow(status, confirmed: confirmed, ended: ended, failure: failure, cancelReason: cancelled ? "0" : "NULL")}
            SELECT [Status] FROM [Rebalances] WHERE [Id] = SCOPE_IDENTITY();
            """)).ShouldBe(int.Parse(status));
    }

    [TestCase("0")]
    [TestCase("1")]
    [TestCase("2")]
    public async Task ASecondOpenRebalance_OnOneAccount_Violates(string secondStatus)
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {RebalanceRow("0")}
            {RebalanceRow(secondStatus, confirmed: secondStatus != "0")}
            """));

        exception.Number.ShouldBe(2601);
        exception.Message.ShouldContain("UX_Rebalances_AccountId_Open");
    }

    [Test]
    public async Task ASecondRebalance_AfterTheFirstEnded_Inserts()
    {
        (await RunAsync($"""
            {World()}
            {RebalanceRow("3", confirmed: true, ended: true)}
            {RebalanceRow("5", ended: true, cancelReason: "1")}
            {RebalanceRow("0")}
            SELECT COUNT(*) FROM [Rebalances] WHERE [AccountId] = @a_brokerage;
            """)).ShouldBe(3);
    }

    [TestCase("status: 8", "CK_Rebalances_Status")]
    [TestCase("trigger: 4", "CK_Rebalances_Trigger")]
    [TestCase("status: 4, confirmed: 1, ended: 1", "CK_Rebalances_Failure")]
    [TestCase("failure: 1", "CK_Rebalances_Failure")]
    [TestCase("status: 1", "CK_Rebalances_Confirmed")]
    [TestCase("status: 3, ended: 1", "CK_Rebalances_Confirmed")]
    [TestCase("confirmedHalf: 1", "CK_Rebalances_Confirmed")]
    [TestCase("status: 5, ended: 1", "CK_Rebalances_CancelReason")]
    [TestCase("cancelReason: 0", "CK_Rebalances_CancelReason")]
    [TestCase("status: 5, ended: 1, cancelReason: 3", "CK_Rebalances_CancelReason")]
    [TestCase("status: 6", "CK_Rebalances_EndedAt")]
    [TestCase("ended: 1", "CK_Rebalances_EndedAt")]
    [TestCase("waves: 2, buyWaves: 3", "CK_Rebalances_Waves")]
    [TestCase("waves: 1, buyWaves: 2", "CK_Rebalances_Waves")]
    public async Task ARebalanceOutsideTheChecks_Violates(string change, string constraint)
    {
        var values = Values(change);
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {RebalanceRow(
                values.GetValueOrDefault("status", "0"),
                trigger: values.GetValueOrDefault("trigger", "1"),
                confirmed: values.GetValueOrDefault("confirmed", "0") == "1",
                confirmedHalf: values.GetValueOrDefault("confirmedHalf", "0") == "1",
                ended: values.GetValueOrDefault("ended", "0") == "1",
                failure: values.GetValueOrDefault("failure", "0") == "1",
                cancelReason: values.GetValueOrDefault("cancelReason", "NULL"),
                waves: values.GetValueOrDefault("waves", "0"),
                buyWaves: values.GetValueOrDefault("buyWaves", "0"))}
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain(constraint);
    }

    [Test]
    public async Task ARebalanceOfAnotherTenantsAssignment_Violates()
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {ScratchSchemaDatabase.TenantRows("b", Guid.NewGuid().ToString("N")[..12])}
            INSERT INTO [AccountPortfolioAssignments] ([PublicId], [TenantId], [AccountId], [PortfolioId], [Status], [AssignedAt], [AssignedBy], [Created], [CreatedBy])
            VALUES (NEWID(), @b_tenant, @b_brokerage, @b_portfolio, 0, SYSDATETIMEOFFSET(), N'test', SYSDATETIMEOFFSET(), N'test');
            DECLARE @b_assignment int = SCOPE_IDENTITY();
            INSERT INTO [Rebalances]
                ([PublicId], [TenantId], [AccountId], [PortfolioId], [AssignmentId], [Trigger], [Status], [BaseCurrency],
                 [MinimumTrade], [CashResidualPercent], [Waves], [BuyWaves], [ProposedAt], [ExpiresAt], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @a_brokerage, @a_portfolio, @b_assignment, 1, 0, 'NZD',
                    100, 1, 0, 0, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'test');
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain("FK_Rebalances_AccountPortfolioAssignments_TenantId_AssignmentId");
    }

    [Test]
    public async Task ALineWithATrade_AndOneWithout_Insert()
    {
        (await RunAsync($"""
            {World()}
            {RebalanceRow("0")}
            DECLARE @rebalance int = SCOPE_IDENTITY();
            {LineRow("@a_instrument", trade: true)}
            {LineRow("@a_other", trade: false)}
            SELECT COUNT(*) FROM [RebalanceLines] WHERE [RebalanceId] = @rebalance;
            """)).ShouldBe(2);
    }

    [TestCase("[Side] = NULL")]
    [TestCase("[TradeQuantity] = NULL")]
    [TestCase("[ReferencePrice] = NULL")]
    [TestCase("[ReferencePriceDate] = NULL")]
    [TestCase("[EstimatedGross] = NULL")]
    [TestCase("[EstimatedFee] = NULL")]
    public async Task ALineWithOnlyPartOfATrade_Violates(string clear)
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {RebalanceRow("0")}
            DECLARE @rebalance int = SCOPE_IDENTITY();
            {LineRow("@a_instrument", trade: true)}
            UPDATE [RebalanceLines] SET {clear};
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain("CK_RebalanceLines_Trade");
    }

    [Test]
    public async Task ASecondLineOrTarget_ForOneInstrument_Violates()
    {
        var line = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {RebalanceRow("0")}
            DECLARE @rebalance int = SCOPE_IDENTITY();
            {LineRow("@a_instrument", trade: true)}
            {LineRow("@a_instrument", trade: false)}
            """));
        line.Message.ShouldContain("UQ_RebalanceLines_RebalanceId_InstrumentId");

        var target = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {RebalanceRow("0")}
            DECLARE @rebalance int = SCOPE_IDENTITY();
            {TargetRow("@a_other", 0)}
            {TargetRow("@a_other", 1)}
            """));
        target.Message.ShouldContain("UQ_RebalanceTargets_RebalanceId_InstrumentId");
    }

    [Test]
    public async Task AnOrderWithARebalance_Inserts_AndOneWithAnotherTenantsRebalance_Violates()
    {
        (await RunAsync($"""
            {World()}
            {RebalanceRow("1", confirmed: true, waves: "1")}
            DECLARE @rebalance int = SCOPE_IDENTITY();
            {OrderRow(rebalance: "@rebalance")}
            SELECT COUNT(*) FROM [Orders] WHERE [RebalanceId] = @rebalance;
            """)).ShouldBe(1);

        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {ScratchSchemaDatabase.TenantRows("b", Guid.NewGuid().ToString("N")[..12])}
            INSERT INTO [AccountPortfolioAssignments] ([PublicId], [TenantId], [AccountId], [PortfolioId], [Status], [AssignedAt], [AssignedBy], [Created], [CreatedBy])
            VALUES (NEWID(), @b_tenant, @b_brokerage, @b_portfolio, 0, SYSDATETIMEOFFSET(), N'test', SYSDATETIMEOFFSET(), N'test');
            INSERT INTO [Rebalances]
                ([PublicId], [TenantId], [AccountId], [PortfolioId], [AssignmentId], [Trigger], [Status], [BaseCurrency],
                 [MinimumTrade], [CashResidualPercent], [Waves], [BuyWaves], [ProposedAt], [ExpiresAt], [Created], [CreatedBy])
            VALUES (NEWID(), @b_tenant, @b_brokerage, @b_portfolio, SCOPE_IDENTITY(), 1, 0, 'NZD',
                    100, 1, 0, 0, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'test');
            DECLARE @other int = SCOPE_IDENTITY();
            {OrderRow(rebalance: "@other")}
            """));
        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain("FK_Orders_Rebalances_TenantId_RebalanceId");
    }

    [Test]
    public async Task EfRoundTrip_KeepsARebalance_ItsTargetsAndLines_AndTheRebalanceOfAnOrder()
    {
        await using var db = await ScratchSchemaDatabase.CreateAsync("RebalancesEf");
        await db.ApplyAllAsync();
        var row = (await db.ReadAsync($"""
            {World()}
            SELECT @a_tenant, @a_brokerage, @a_portfolio, @a_assignment, @a_instrument, @a_other, @a_adviser;
            """)).Single();
        var ids = row.Select(Convert.ToInt32).ToArray();
        var (tenantId, accountId, portfolioId, assignmentId, instrumentId, otherId, userId) =
            (ids[0], ids[1], ids[2], ids[3], ids[4], ids[5], ids[6]);
        var at = new DateTimeOffset(2026, 11, 23, 2, 10, 0, TimeSpan.Zero);
        var day = new DateOnly(2026, 11, 23);
        var publicId = Guid.NewGuid();
        int rebalanceId;
        await using (var context = OpenDb(db.ConnectionString))
        {
            var rebalance = Rebalance.Propose(
                new RebalanceSubject(tenantId, accountId, portfolioId, assignmentId, RebalanceTrigger.Assign, "NZD",
                    100m, 1m, [new RebalanceTargetSpec(otherId, 100m)], userId, publicId),
                10000m, 9000m,
                [
                    new RebalanceLineSpec(instrumentId, 40m, 10m, null, "USD",
                        new RebalanceLineTrade(OrderSide.Sell, 40m, 25m, day, 1000m, 5m)),
                    new RebalanceLineSpec(otherId, 0m, 0m, 100m, "USD",
                        new RebalanceLineTrade(OrderSide.Buy, 59.4m, 100m, day, 5940m, 29.7m))
                ],
                at);
            rebalance.CreatedBy = "test";
            context.Set<Rebalance>().Add(rebalance);
            await context.SaveChangesAsync();
            rebalanceId = rebalance.Id;
        }

        await using (var context = OpenDb(db.ConnectionString))
        {
            var rebalance = await context.Set<Rebalance>().SingleAsync(item => item.Id == rebalanceId);
            rebalance.RowVersion.ShouldNotBeEmpty();
            rebalance.Confirm(userId, "simulator", at.AddMinutes(5));
            rebalance.StartWave(OrderSide.Sell);
            rebalance.MarkFollowUp();
            var order = Order.Place(tenantId, accountId, instrumentId, OrderSide.Sell, 40m, "USD", 25m, day,
                OrderFeeTerms.None, "simulator", "Rebalance", userId, at.AddMinutes(5), rebalanceId: rebalanceId);
            order.CreatedBy = "test";
            context.Set<Order>().Add(order);
            await context.SaveChangesAsync();
        }

        await using (var context = OpenDb(db.ConnectionString))
        {
            var stored = await context.Set<Rebalance>().AsNoTracking()
                .Include(item => item.Targets)
                .Include(item => item.Lines)
                .SingleAsync(item => item.Id == rebalanceId);
            (stored.PublicId, stored.TenantId, stored.AccountId, stored.PortfolioId, stored.AssignmentId)
                .ShouldBe((publicId, tenantId, accountId, portfolioId, assignmentId));
            (stored.Trigger, stored.Status, stored.BaseCurrency, stored.TotalValue, stored.Cash)
                .ShouldBe((RebalanceTrigger.Assign, RebalanceStatus.Selling, "NZD", (decimal?)10000m, (decimal?)9000m));
            (stored.MinimumTrade, stored.CashResidualPercent, stored.Connection, stored.Waves, stored.BuyWaves)
                .ShouldBe((100m, 1m, "simulator", 1, 0));
            (stored.ProposedByUserId, stored.ProposedAt, stored.ExpiresAt)
                .ShouldBe(((int?)userId, at, at.AddHours(24)));
            (stored.ConfirmedByUserId, stored.ConfirmedAt, stored.FollowUp, stored.EndedAt)
                .ShouldBe(((int?)userId, (DateTimeOffset?)at.AddMinutes(5), true, (DateTimeOffset?)null));
            stored.Targets.Select(target => (target.InstrumentId, target.TargetWeight, target.SortOrder))
                .ShouldBe([(otherId, 100m, 0)]);
            var sell = stored.Lines.Single(line => line.InstrumentId == instrumentId);
            (sell.Quantity, sell.Weight, sell.TargetWeight, sell.Currency).ShouldBe((40m, 10m, (decimal?)null, "USD"));
            (sell.Side, sell.TradeQuantity, sell.ReferencePrice, sell.ReferencePriceDate, sell.EstimatedGross, sell.EstimatedFee)
                .ShouldBe(((OrderSide?)OrderSide.Sell, (decimal?)40m, (decimal?)25m, (DateOnly?)day, (decimal?)1000m, (decimal?)5m));
            stored.Lines.Single(line => line.InstrumentId == otherId).TargetWeight.ShouldBe(100m);
            (await context.Set<Order>().AsNoTracking().SingleAsync()).RebalanceId.ShouldBe(rebalanceId);
        }
    }

    private static Dictionary<string, string> Values(string change) =>
        change.Split(", ").Select(part => part.Split(": ")).ToDictionary(pair => pair[0], pair => pair[1]);

    /// <summary>A tenant (<c>@a_*</c>) with an Active assignment <c>@a_assignment</c> of its portfolio on its
    /// brokerage account.</summary>
    private static string World(bool withAssignment = true)
    {
        const string assignment = """
            INSERT INTO [AccountPortfolioAssignments] ([PublicId], [TenantId], [AccountId], [PortfolioId], [Status], [AssignedAt], [AssignedBy], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @a_brokerage, @a_portfolio, 0, SYSDATETIMEOFFSET(), N'test', SYSDATETIMEOFFSET(), N'test');
            DECLARE @a_assignment int = SCOPE_IDENTITY();
            """;
        return ScratchSchemaDatabase.TenantRows("a", Guid.NewGuid().ToString("N")[..12]) + "\n"
            + (withAssignment ? assignment : "") + "\n";
    }

    private static string RebalanceRow(
        string status,
        string trigger = "1",
        bool confirmed = false,
        bool confirmedHalf = false,
        bool ended = false,
        bool failure = false,
        string cancelReason = "NULL",
        string waves = "0",
        string buyWaves = "0") => $"""
        INSERT INTO [Rebalances]
            ([PublicId], [TenantId], [AccountId], [PortfolioId], [AssignmentId], [Trigger], [Status], [BaseCurrency],
             [TotalValue], [Cash], [MinimumTrade], [CashResidualPercent], [Connection], [Waves], [BuyWaves],
             [FailureCode], [FailureMessage], [ProposedByUserId], [ProposedAt], [ExpiresAt],
             [ConfirmedByUserId], [ConfirmedAt], [CancelReason], [EndedAt], [Created], [CreatedBy])
        VALUES (NEWID(), @a_tenant, @a_brokerage, @a_portfolio, @a_assignment, {trigger}, {status}, 'NZD',
                10000, 10000, 100, 1, {(confirmed ? "N'simulator'" : "NULL")}, {waves}, {buyWaves},
                {(failure ? "N'missing_price'" : "NULL")}, {(failure ? "N'Missing price for VTI.'" : "NULL")},
                @a_adviser, SYSDATETIMEOFFSET(), DATEADD(HOUR, 24, SYSDATETIMEOFFSET()),
                {(confirmed || confirmedHalf ? "@a_adviser" : "NULL")}, {(confirmed ? "SYSDATETIMEOFFSET()" : "NULL")},
                {cancelReason}, {(ended ? "SYSDATETIMEOFFSET()" : "NULL")}, SYSDATETIMEOFFSET(), N'test');
        """;

    private static string TargetRow(string instrument, int sortOrder) => $"""
        INSERT INTO [RebalanceTargets] ([TenantId], [RebalanceId], [InstrumentId], [TargetWeight], [SortOrder])
        VALUES (@a_tenant, @rebalance, {instrument}, 50, {sortOrder});
        """;

    private static string LineRow(string instrument, bool trade) => $"""
        INSERT INTO [RebalanceLines]
            ([TenantId], [RebalanceId], [InstrumentId], [Quantity], [Weight], [TargetWeight], [Side], [TradeQuantity],
             [ReferencePrice], [ReferencePriceDate], [EstimatedGross], [EstimatedFee], [Currency])
        VALUES (@a_tenant, @rebalance, {instrument}, 0, 0, 50,
                {(trade ? "0, 59.4, 100, '2026-11-23', 5940, 29.7" : "NULL, NULL, NULL, NULL, NULL, NULL")}, 'USD');
        """;

    private static string OrderRow(string? rebalance) => $"""
        INSERT INTO [Orders]
            ([PublicId], [TenantId], [AccountId], [InstrumentId], [Side], [Type], [TimeInForce], [Quantity], [Currency],
             [ReferencePrice], [ReferencePriceDate], [PriceLimit], [Connection], [Status], [FilledQuantity], [PostedGross],
             [FeeCharged], [ReservedCash], [ReservedQuantity], [PlacedByUserId], [PlacedAt], [Attempts], [Created], [CreatedBy]
             {(rebalance is null ? "" : ", [RebalanceId]")})
        VALUES (NEWID(), @a_tenant, @a_brokerage, @a_instrument, 0, 0, 0, 10, 'USD',
                100, '2026-11-23', 105, N'simulator', 0, 0, 0,
                0, 0, 0, @a_adviser, SYSDATETIMEOFFSET(), 0, SYSDATETIMEOFFSET(), N'test'
                {(rebalance is null ? "" : $", {rebalance}")});
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
