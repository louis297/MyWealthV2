using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyWealthV2.Domain.CorporateActions;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Schema;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

/// <summary>Corporate actions §6.1: the announcements, corporate actions, entitlements and the widened checks
/// (script 0050).</summary>
public class CorporateActionsSchemaTests
{
    private const string Script = "0050_corporate_actions.sql";

    [Test]
    public void SchemaDirectory_Includes0050Script()
    {
        new FileSchemaScriptSource(ScratchSchemaDatabase.SchemaDirectory()).GetScripts()
            .Select(script => script.Name)
            .ShouldContain(Script);
    }

    [Test]
    public async Task AnAnnouncement_Inserts_AndASecondWithTheSameConnectionAndOutsideId_Violates()
    {
        (await RunAsync($"""
            {Announcement("o-1")}
            {Announcement("o-1", connection: "other")}
            SELECT COUNT(*) FROM [CorporateActionAnnouncements] WHERE [OutsideId] = N'o-1';
            """)).ShouldBe(2);

        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {Announcement("o-1")}
            {Announcement("o-1")}
            """));
        exception.Number.ShouldBe(2601);
        exception.Message.ShouldContain("UX_CorporateActionAnnouncements_Connection_OutsideId");
    }

    [Test]
    public async Task AnAnnouncementWithNoKind_Inserts()
    {
        (await RunAsync($"""
            {Announcement("o-2", kind: "NULL")}
            SELECT COUNT(*) FROM [CorporateActionAnnouncements] WHERE [OutsideId] = N'o-2' AND [Kind] IS NULL;
            """)).ShouldBe(1);
    }

    [TestCase("kind: 4", "CK_CorporateActionAnnouncements_Kind")]
    [TestCase("partyStatus: 3", "CK_CorporateActionAnnouncements_PartyStatus")]
    [TestCase("version: 0", "CK_CorporateActionAnnouncements_Version")]
    [TestCase("ratioHeld: 0", "CK_CorporateActionAnnouncements_Terms")]
    [TestCase("status: 3", "CK_CorporateActionAnnouncements_Status")]
    [TestCase("status: 2", "CK_CorporateActionAnnouncements_Skipped")]
    [TestCase("skipReason: 1", "CK_CorporateActionAnnouncements_Skipped")]
    [TestCase("status: 2, skipReason: 5", "CK_CorporateActionAnnouncements_SkipReason")]
    public async Task AnAnnouncementOutsideTheChecks_Violates(string change, string constraint)
    {
        var values = change.Split(", ").Select(part => part.Split(": ")).ToDictionary(pair => pair[0], pair => pair[1]);
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync(Announcement(
            "o-3",
            kind: values.GetValueOrDefault("kind", "0"),
            partyStatus: values.GetValueOrDefault("partyStatus", "0"),
            version: values.GetValueOrDefault("version", "1"),
            ratioHeld: values.GetValueOrDefault("ratioHeld", "1"),
            status: values.GetValueOrDefault("status", "0"),
            skipReason: values.GetValueOrDefault("skipReason", "NULL"))));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain(constraint);
    }

    [Test]
    public async Task ASecondFeedAction_ForTheSameInstrumentConnectionAndPartyActionId_Violates()
    {
        (await RunAsync($"""
            {World()}
            {FeedAction("w-CA1")}
            {FeedAction("w-CA2")}
            SELECT COUNT(*) FROM [CorporateActions] WHERE [TenantId] = @a_tenant;
            """)).ShouldBe(2);

        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {FeedAction("w-CA1")}
            {FeedAction("w-CA1")}
            """));
        exception.Number.ShouldBe(2601);
        exception.Message.ShouldContain("UX_CorporateActions_InstrumentId_Connection_PartyActionId");
    }

    [Test]
    public async Task ASecondLiveManualAction_ForTheSameInstrumentKindAndExDate_Violates_AndInsertsAfterACancel()
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {ManualAction()}
            {ManualAction()}
            """));
        exception.Number.ShouldBe(2601);
        exception.Message.ShouldContain("UX_CorporateActions_InstrumentId_Kind_ExDate");

        (await RunAsync($"""
            {World()}
            {ManualAction(status: 3)}
            {ManualAction()}
            {ManualAction(kind: 2, ratioNew: 1, ratioHeld: 5)}
            SELECT COUNT(*) FROM [CorporateActions] WHERE [TenantId] = @a_tenant;
            """)).ShouldBe(3);
    }

    [TestCase("kind: 4", "CK_CorporateActions_Kind")]
    [TestCase("ratioNew: 10001", "CK_CorporateActions_Terms")]
    [TestCase("ratioHeld: 0", "CK_CorporateActions_Terms")]
    [TestCase("origin: 2", "CK_CorporateActions_Origin")]
    [TestCase("origin: 1", "CK_CorporateActions_Feed")]
    [TestCase("status: 4", "CK_CorporateActions_Status")]
    [TestCase("status: 1", "CK_CorporateActions_EffectiveAt")]
    [TestCase("status: 2, effective: 1, applied: 0", "CK_CorporateActions_AppliedAt")]
    [TestCase("status: 1, effective: 1, applied: 1", "CK_CorporateActions_AppliedAt")]
    [TestCase("effective: 1", "CK_CorporateActions_EffectiveAt")]
    [TestCase("status: 0, cancelled: 1", "CK_CorporateActions_CancelledAt")]
    public async Task ACorporateActionOutsideTheChecks_Violates(string change, string constraint)
    {
        var values = change.Split(", ").Select(part => part.Split(": ")).ToDictionary(pair => pair[0], pair => int.Parse(pair[1]));
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {ManualAction(
                kind: values.GetValueOrDefault("kind", 0),
                ratioNew: values.GetValueOrDefault("ratioNew", 2),
                ratioHeld: values.GetValueOrDefault("ratioHeld", 1),
                origin: values.GetValueOrDefault("origin", 0),
                status: values.GetValueOrDefault("status", 0),
                effectiveAt: values.GetValueOrDefault("effective", 0) == 1,
                cancelledAt: values.GetValueOrDefault("cancelled", 0) == 1,
                appliedAt: values.TryGetValue("applied", out var applied) ? applied == 1 : null)}
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain(constraint);
    }

    [Test]
    public async Task AnEntitlement_IsOnePerActionAndAccount()
    {
        (await RunAsync($"""
            {World()}
            {ManualAction(status: 1, effectiveAt: true)}
            {Entitlement("@a_brokerage")}
            {Entitlement("@a_bank")}
            SELECT COUNT(*) FROM [CorporateActionEntitlements] WHERE [CorporateActionId] = (SELECT [Id] FROM #action);
            """)).ShouldBe(2);

        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {ManualAction(status: 1, effectiveAt: true)}
            {Entitlement("@a_brokerage")}
            {Entitlement("@a_brokerage")}
            """));
        exception.Number.ShouldBe(2627);
        exception.Message.ShouldContain("UQ_CorporateActionEntitlements_CorporateActionId_AccountId");
    }

    [Test]
    public async Task APostedAndANotAppliedEntitlement_Insert()
    {
        (await RunAsync($"""
            {World()}
            {ManualAction(status: 1, effectiveAt: true)}
            {Entitlement("@a_brokerage", status: 1, transaction: "@a_buy", quantities: true, postedAt: true)}
            {Entitlement("@a_bank", status: 2, reason: "1", postedAt: true)}
            SELECT COUNT(*) FROM [CorporateActionEntitlements] WHERE [CorporateActionId] = (SELECT [Id] FROM #action) AND [Status] > 0;
            """)).ShouldBe(2);
    }

    [TestCase("status: 3", "CK_CorporateActionEntitlements_Status")]
    [TestCase("status: 2, postedAt: 1", "CK_CorporateActionEntitlements_NotApplied")]
    [TestCase("reason: 0", "CK_CorporateActionEntitlements_NotApplied")]
    [TestCase("status: 2, reason: 3, postedAt: 1", "CK_CorporateActionEntitlements_Reason")]
    [TestCase("status: 1, postedAt: 1", "CK_CorporateActionEntitlements_Posted")]
    [TestCase("quantities: 1", "CK_CorporateActionEntitlements_Posted")]
    [TestCase("transaction: 1", "CK_CorporateActionEntitlements_Posted")]
    [TestCase("postedAt: 1", "CK_CorporateActionEntitlements_PostedAt")]
    [TestCase("status: 1, transaction: 1, quantities: 1", "CK_CorporateActionEntitlements_PostedAt")]
    public async Task AnEntitlementOutsideTheChecks_Violates(string change, string constraint)
    {
        var values = change.Split(", ").Select(part => part.Split(": ")).ToDictionary(pair => pair[0], pair => int.Parse(pair[1]));
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {ManualAction(status: 1, effectiveAt: true)}
            {Entitlement(
                "@a_brokerage",
                status: values.GetValueOrDefault("status", 0),
                reason: values.TryGetValue("reason", out var reason) ? reason.ToString() : "NULL",
                transaction: values.GetValueOrDefault("transaction", 0) == 1 ? "@a_buy" : "NULL",
                quantities: values.GetValueOrDefault("quantities", 0) == 1,
                postedAt: values.GetValueOrDefault("postedAt", 0) == 1)}
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain(constraint);
    }

    [Test]
    public async Task SourceKind7_NeedsASourcePublicId_And12Violates()
    {
        (await RunAsync($"""
            {World()}
            {InsertTransaction(7, "NEWID()")}
            SELECT [SourceKind] FROM [Transactions] WHERE [Id] = SCOPE_IDENTITY();
            """)).ShouldBe(7);

        var noId = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertTransaction(7, "NULL")}
            """));
        noId.Number.ShouldBe(547);
        noId.Message.ShouldContain("CK_Transactions_SourcePublicId");

        var nine = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertTransaction(12, "NEWID()")}
            """));
        nine.Number.ShouldBe(547);
        nine.Message.ShouldContain("CK_Transactions_SourceKind");
    }

    [Test]
    public async Task ACorporateActionDetail_IsOnePerTransaction()
    {
        (await RunAsync($"""
            {World()}
            {InsertTransaction(7, "NEWID()")}
            DECLARE @split int = SCOPE_IDENTITY();
            {Detail("@split")}
            SELECT [QuantityAfter] FROM [TransactionCorporateActionDetails] WHERE [TransactionId] = @split;
            """)).ShouldBe(200m);

        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {Detail("@a_buy")}
            {Detail("@a_buy")}
            """));
        exception.Number.ShouldBe(2627);
        exception.Message.ShouldContain("PK_TransactionCorporateActionDetails");
    }

    [TestCase("3", "2", "100", "200", "CK_TransactionCorporateActionDetails_Kind")]
    [TestCase("0", "0", "100", "200", "CK_TransactionCorporateActionDetails_Ratio")]
    [TestCase("0", "2", "0", "200", "CK_TransactionCorporateActionDetails_Quantities")]
    [TestCase("0", "2", "100", "0", "CK_TransactionCorporateActionDetails_Quantities")]
    public async Task ADetailOutsideTheChecks_Violates(string kind, string ratioNew, string before, string after, string constraint)
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {Detail("@a_buy", kind, ratioNew, before, after)}
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain(constraint);
    }

    [Test]
    public async Task TheCorporateActionsFeed_IsAllowed_AndInvalidItemIsAFailure()
    {
        (await RunAsync("""
            INSERT INTO [MarketDataFeeds] ([Connection], [Kind], [Cursor], [LastFailure]) VALUES (N'simulator', 1, N'w:3', 5);
            SELECT [Kind] FROM [MarketDataFeeds] WHERE [Connection] = N'simulator' AND [Kind] = 1;
            """)).ShouldBe(1);
    }

    [Test]
    public async Task EfRoundTrip_KeepsACorporateActionAndItsEntitlements()
    {
        await using var db = await ScratchSchemaDatabase.CreateAsync("CorporateActionEf");
        await db.ApplyAllAsync();
        var row = (await db.ReadAsync($"""
            {ScratchSchemaDatabase.TenantRows("a", Guid.NewGuid().ToString("N")[..12])}
            SELECT @a_tenant, @a_instrument, @a_brokerage, @a_buy, @a_adviser;
            """)).Single();
        var (tenantId, instrumentId, accountId, transactionId, userId) =
            (Convert.ToInt32(row[0]), Convert.ToInt32(row[1]), Convert.ToInt32(row[2]), Convert.ToInt32(row[3]), Convert.ToInt32(row[4]));
        var at = new DateTimeOffset(2026, 11, 22, 3, 41, 0, TimeSpan.Zero);
        var manual = CorporateAction.Enter(
            tenantId, instrumentId, CorporateActionKind.Split, new CorporateActionRatio(4, 1), new DateOnly(2026, 12, 1),
            new DateOnly(2026, 12, 2), null, "Announced");
        manual.CreatedBy = "test";
        var feed = CorporateAction.Announce(
            tenantId, instrumentId, "simulator", "w-CA1", 2, CorporateActionKind.Bonus, new CorporateActionRatio(1, 5),
            new DateOnly(2026, 12, 3), null, new DateOnly(2026, 12, 9));
        feed.CreatedBy = "test";
        await using (var context = OpenDb(db.ConnectionString))
        {
            context.Set<CorporateAction>().AddRange(manual, feed);
            await context.SaveChangesAsync();
        }

        await using (var context = OpenDb(db.ConnectionString))
        {
            var stored = await context.Set<CorporateAction>().SingleAsync(action => action.Id == manual.Id);
            stored.RowVersion.ShouldNotBeEmpty();
            stored.MakeEffective(at, userId);
            var posted = CorporateActionEntitlement.Create(tenantId, stored.Id, accountId);
            posted.MarkPosted(100m, 400m, transactionId, at.AddSeconds(1));
            context.Set<CorporateActionEntitlement>().Add(posted);
            await context.SaveChangesAsync();
            stored.MarkApplied(at.AddSeconds(2));
            await context.SaveChangesAsync();
        }

        await using (var context = OpenDb(db.ConnectionString))
        {
            var stored = await context.Set<CorporateAction>().AsNoTracking().SingleAsync(action => action.Id == manual.Id);
            stored.PublicId.ShouldBe(manual.PublicId);
            (stored.Kind, stored.RatioNew, stored.RatioHeld, stored.ExDate, stored.RecordDate, stored.PaymentDate)
                .ShouldBe((CorporateActionKind.Split, 4, 1, new DateOnly(2026, 12, 1), (DateOnly?)new DateOnly(2026, 12, 2), (DateOnly?)null));
            (stored.Origin, stored.Status, stored.Memo, stored.EffectiveByUserId)
                .ShouldBe((CorporateActionOrigin.Manual, CorporateActionStatus.Applied, "Announced", (int?)userId));
            stored.EffectiveAt.ShouldBe(at);
            stored.AppliedAt.ShouldBe(at.AddSeconds(2));

            var storedFeed = await context.Set<CorporateAction>().AsNoTracking().SingleAsync(action => action.Id == feed.Id);
            (storedFeed.Origin, storedFeed.Connection, storedFeed.PartyActionId, storedFeed.FeedVersion, storedFeed.Kind)
                .ShouldBe((CorporateActionOrigin.Feed, "simulator", "w-CA1", (int?)2, CorporateActionKind.Bonus));

            var entitlement = await context.Set<CorporateActionEntitlement>().AsNoTracking().SingleAsync(item => item.CorporateActionId == manual.Id);
            (entitlement.AccountId, entitlement.Status, entitlement.QuantityBefore, entitlement.QuantityAfter, entitlement.TransactionId)
                .ShouldBe((accountId, EntitlementStatus.Posted, (decimal?)100m, (decimal?)400m, (int?)transactionId));
            entitlement.PostedAt.ShouldBe(at.AddSeconds(1));
        }
    }

    [Test]
    public async Task EfRoundTrip_KeepsAnEntitlementPostingAndItsDetail()
    {
        await using var db = await ScratchSchemaDatabase.CreateAsync("CorporateActionPostingEf");
        await db.ApplyAllAsync();
        var row = (await db.ReadAsync($"""
            {ScratchSchemaDatabase.TenantRows("a", Guid.NewGuid().ToString("N")[..12])}
            SELECT @a_tenant, @a_instrument, @a_brokerage;
            """)).Single();
        var (tenantId, instrumentId, accountId) = (Convert.ToInt32(row[0]), Convert.ToInt32(row[1]), Convert.ToInt32(row[2]));
        var actionId = Guid.NewGuid();
        var posting = Transaction.PostCorporateAction(
            tenantId, accountId, instrumentId, "USD", CorporateActionKind.Consolidation, new CorporateActionRatio(1, 10),
            new DateOnly(2026, 11, 24), null, new DateOnly(2026, 12, 1), 105m, 10.5m,
            new DateTimeOffset(2026, 11, 22, 3, 41, 2, TimeSpan.Zero), MyWealthV2.Domain.ValueObjects.TransactionSource.CorporateAction(actionId));
        posting.CreatedBy = "worker";
        foreach (var leg in posting.SecurityLegs)
        {
            leg.CreatedBy = "worker";
        }

        await using (var context = OpenDb(db.ConnectionString))
        {
            context.Set<Transaction>().Add(posting);
            await context.SaveChangesAsync();
        }

        await using (var context = OpenDb(db.ConnectionString))
        {
            var stored = await context.Set<Transaction>().AsNoTracking()
                .Include(transaction => transaction.SecurityLegs)
                .Include(transaction => transaction.CorporateActionDetail)
                .SingleAsync(transaction => transaction.Id == posting.Id);
            (stored.Type, stored.SourceKind, stored.SourcePublicId, stored.Memo)
                .ShouldBe((TransactionType.Split, TransactionSourceKind.CorporateAction, (Guid?)actionId, "Consolidation 1 for 10"));
            stored.SecurityLegs.Single().Quantity.ShouldBe(-94.5m);
            var detail = stored.CorporateActionDetail.ShouldNotBeNull();
            (detail.Kind, detail.RatioNew, detail.RatioHeld, detail.ExDate, detail.RecordDate, detail.PaymentDate, detail.QuantityBefore, detail.QuantityAfter)
                .ShouldBe((CorporateActionKind.Consolidation, 1, 10, new DateOnly(2026, 11, 24), (DateOnly?)null,
                    (DateOnly?)new DateOnly(2026, 12, 1), 105m, 10.5m));
        }
    }

    [Test]
    public async Task EfRoundTrip_KeepsAnAnnouncementAndTheFeedCursor()
    {
        await using var db = await ScratchSchemaDatabase.CreateAsync("CorporateActionAnnouncementEf");
        await db.ApplyAllAsync();
        var at = new DateTimeOffset(2026, 11, 22, 3, 15, 2, TimeSpan.Zero);
        var announcement = CorporateActionAnnouncement.Receive(
            "simulator", "w-CA1-V1", "w-CA1", 1, PartyActionStatus.Effective, "ACME.SIM", "USD", null, 2, 1,
            new DateOnly(2026, 11, 24), new DateOnly(2026, 11, 25), null, at);
        var feed = MarketDataFeed.Start("simulator", MarketDataFeedKind.CorporateActions);
        feed.Advance("w:1", at);
        await using (var context = OpenDb(db.ConnectionString))
        {
            context.Set<CorporateActionAnnouncement>().Add(announcement);
            context.Set<MarketDataFeed>().Add(feed);
            await context.SaveChangesAsync();
        }

        await using (var context = OpenDb(db.ConnectionString))
        {
            var stored = await context.Set<CorporateActionAnnouncement>().SingleAsync(row => row.Id == announcement.Id);
            stored.Skip(AnnouncementSkipReason.UnsupportedKind, at.AddSeconds(1));
            await context.SaveChangesAsync();
        }

        await using (var context = OpenDb(db.ConnectionString))
        {
            var stored = await context.Set<CorporateActionAnnouncement>().AsNoTracking().SingleAsync(row => row.Id == announcement.Id);
            (stored.PublicId, stored.Connection, stored.OutsideId, stored.PartyActionId, stored.Version, stored.PartyStatus, stored.Kind)
                .ShouldBe((announcement.PublicId, "simulator", "w-CA1-V1", "w-CA1", 1, PartyActionStatus.Effective, (CorporateActionKind?)null));
            (stored.Symbol, stored.Currency, stored.RatioNew, stored.RatioHeld, stored.RecordDate)
                .ShouldBe(("ACME.SIM", "USD", 2, 1, (DateOnly?)new DateOnly(2026, 11, 25)));
            (stored.Status, stored.SkipReason, stored.ReceivedAt).ShouldBe(
                (AnnouncementStatus.Skipped, (AnnouncementSkipReason?)AnnouncementSkipReason.UnsupportedKind, at));
            var storedFeed = await context.Set<MarketDataFeed>().AsNoTracking()
                .SingleAsync(row => row.Connection == "simulator" && row.Kind == MarketDataFeedKind.CorporateActions);
            storedFeed.Cursor.ShouldBe("w:1");
        }
    }

    /// <summary>A tenant (<c>@a_*</c> from <see cref="ScratchSchemaDatabase.TenantRows"/>).</summary>
    private static string World() => $"""
        {ScratchSchemaDatabase.TenantRows("a", Guid.NewGuid().ToString("N")[..12])}
        CREATE TABLE #action ([Id] int);
        """;

    private static string Announcement(
        string outsideId,
        string connection = "simulator",
        string kind = "0",
        string partyStatus = "0",
        string version = "1",
        string ratioHeld = "1",
        string status = "0",
        string skipReason = "NULL") => $"""
        INSERT INTO [CorporateActionAnnouncements]
            ([PublicId], [Connection], [OutsideId], [PartyActionId], [Version], [PartyStatus], [Symbol], [Currency], [Kind],
             [RatioNew], [RatioHeld], [ExDate], [ReceivedAt], [Status], [SkipReason])
        VALUES (NEWID(), N'{connection}', N'{outsideId}', N'w-CA1', {version}, {partyStatus}, N'ACME.SIM', 'USD', {kind},
                2, {ratioHeld}, '2026-12-01', SYSDATETIMEOFFSET(), {status}, {skipReason});
        """;

    private static string FeedAction(string partyActionId) => $"""
        INSERT INTO [CorporateActions]
            ([PublicId], [TenantId], [InstrumentId], [Kind], [RatioNew], [RatioHeld], [ExDate], [Origin], [Connection],
             [PartyActionId], [FeedVersion], [Status], [Created], [CreatedBy])
        VALUES (NEWID(), @a_tenant, @a_instrument, 0, 2, 1, '2026-12-01', 1, N'simulator', N'{partyActionId}', 1, 0,
                SYSDATETIMEOFFSET(), N'test');
        """;

    /// <summary>A corporate action on <c>@a_instrument</c>; its Id is the one row of <c>#action</c>.</summary>
    private static string ManualAction(
        int kind = 0,
        int ratioNew = 2,
        int ratioHeld = 1,
        int origin = 0,
        int status = 0,
        bool effectiveAt = false,
        bool cancelledAt = false,
        bool? appliedAt = null)
    {
        var applied = appliedAt ?? status == 2 ? "SYSDATETIMEOFFSET()" : "NULL";
        var effective = effectiveAt ? "SYSDATETIMEOFFSET()" : "NULL";
        var cancelled = status == 3 || cancelledAt ? "SYSDATETIMEOFFSET()" : "NULL";
        return $"""
            INSERT INTO [CorporateActions]
                ([PublicId], [TenantId], [InstrumentId], [Kind], [RatioNew], [RatioHeld], [ExDate], [Origin], [Status],
                 [EffectiveAt], [AppliedAt], [CancelledAt], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @a_instrument, {kind}, {ratioNew}, {ratioHeld}, '2026-12-01', {origin}, {status},
                    {effective}, {applied}, {cancelled}, SYSDATETIMEOFFSET(), N'test');
            DELETE FROM #action; INSERT INTO #action VALUES (SCOPE_IDENTITY());
            """;
    }

    private static string Entitlement(
        string account,
        int status = 0,
        string reason = "NULL",
        string transaction = "NULL",
        bool quantities = false,
        bool postedAt = false) => $"""
        INSERT INTO [CorporateActionEntitlements]
            ([TenantId], [CorporateActionId], [AccountId], [Status], [Reason], [QuantityBefore], [QuantityAfter], [TransactionId], [PostedAt])
        SELECT @a_tenant, [Id], {account}, {status}, {reason}, {(quantities ? "100" : "NULL")}, {(quantities ? "200" : "NULL")},
               {transaction}, {(postedAt ? "SYSDATETIMEOFFSET()" : "NULL")}
        FROM #action;
        """;

    private static string Detail(string transaction, string kind = "0", string ratioNew = "2", string before = "100", string after = "200") => $"""
        INSERT INTO [TransactionCorporateActionDetails]
            ([TransactionId], [TenantId], [Kind], [RatioNew], [RatioHeld], [ExDate], [QuantityBefore], [QuantityAfter])
        VALUES ({transaction}, @a_tenant, {kind}, {ratioNew}, 1, '2026-12-01', {before}, {after});
        """;

    private static string InsertTransaction(int sourceKind, string sourcePublicId) => $"""
        INSERT INTO [Transactions] ([PublicId], [TenantId], [Type], [SourceKind], [SourcePublicId], [BookedAt], [Created], [CreatedBy])
        VALUES (NEWID(), @a_tenant, 13, {sourceKind}, {sourcePublicId}, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'test');
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
