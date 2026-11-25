using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyWealthV2.Domain.CorporateActions;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.ValueObjects;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Schema;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

/// <summary>Dividends §6.1: the dividend columns and checks of script 0057.</summary>
[Parallelizable]
public class DividendsSchemaTests
{
    private const string Script = "0057_dividends.sql";

    [Test]
    public void SchemaDirectory_Includes0057Script()
    {
        new FileSchemaScriptSource(ScratchSchemaDatabase.SchemaDirectory()).GetScripts()
            .Select(script => script.Name)
            .ShouldContain(Script);
    }

    [Test]
    public async Task ADividend_WithTermsAndNoRatio_Inserts()
    {
        (await RunAsync($"""
            {World()}
            {Action()}
            {Action(kind: 0, ratio: "2, 1", terms: "NULL, NULL, NULL, NULL, NULL", exDate: "2026-12-02")}
            SELECT COUNT(*) FROM [CorporateActions] WHERE [TenantId] = @a_tenant;
            """)).ShouldBe(2);
    }

    [TestCase("kind: 3, ratio: 2 1", "CK_CorporateActions_Terms")]
    [TestCase("kind: 0, ratio: 2 1, terms: dividend", "CK_CorporateActions_Terms")]
    [TestCase("kind: 0, ratio: none, terms: none", "CK_CorporateActions_Terms")]
    [TestCase("kind: 4", "CK_CorporateActions_Kind")]
    [TestCase("amount: 0", "CK_CorporateActions_Terms")]
    [TestCase("taxSource: 2", "CK_CorporateActions_Terms")]
    [TestCase("credit: 0.73", "CK_CorporateActions_Terms")]
    [TestCase("rate: 100", "CK_CorporateActions_Terms")]
    [TestCase("price: 0", "CK_CorporateActions_Terms")]
    public async Task ACorporateActionOutsideTheTermChecks_Violates(string change, string constraint)
    {
        var values = change.Split(", ").Select(part => part.Split(": ")).ToDictionary(pair => pair[0], pair => pair[1]);
        var terms = values.GetValueOrDefault("terms") switch
        {
            "none" => "NULL, NULL, NULL, NULL, NULL",
            _ => $"{values.GetValueOrDefault("amount", "0.72")}, {values.GetValueOrDefault("taxSource", "0")}, "
                 + $"{values.GetValueOrDefault("credit", "0.28")}, {values.GetValueOrDefault("rate", "0")}, "
                 + $"{values.GetValueOrDefault("price", "NULL")}"
        };
        var ratio = values.GetValueOrDefault("ratio") switch
        {
            "2 1" => "2, 1",
            _ => "NULL, NULL"
        };

        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {Action(kind: int.Parse(values.GetValueOrDefault("kind", "3")), ratio: ratio, terms: terms)}
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain(constraint);
    }

    [Test]
    public async Task PaidAt_WithoutAReceipt_Violates()
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {Action()}
            UPDATE [CorporateActions] SET [PaidAt] = SYSDATETIMEOFFSET() WHERE [Id] = (SELECT [Id] FROM #action);
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain("CK_CorporateActions_PaidAt");
    }

    [Test]
    public async Task AnElectionReason_OnAnElectionThatIsNotRejected_Violates()
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {Action()}
            UPDATE [CorporateActions] SET [ElectionStatus] = 2, [ElectionReason] = 1 WHERE [Id] = (SELECT [Id] FROM #action);
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain("CK_CorporateActions_ElectionReason");
    }

    [Test]
    public async Task AnElection_OnASplit_Violates()
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {Action(kind: 0, ratio: "2, 1", terms: "NULL, NULL, NULL, NULL, NULL")}
            UPDATE [CorporateActions] SET [ElectionStatus] = 0 WHERE [Id] = (SELECT [Id] FROM #action);
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain("CK_CorporateActions_DividendOnly");
    }

    [Test]
    public async Task OneLine_AsTheReceiptOfTwoDividends_Violates()
    {
        (await RunAsync($"""
            {World()}
            {Line()}
            {Action()}
            UPDATE [CorporateActions] SET [ReceiptLineId] = @line, [ReceivedAmount] = 103 WHERE [Id] = (SELECT [Id] FROM #action);
            SELECT COUNT(*) FROM [CorporateActions] WHERE [ReceiptLineId] = @line;
            """)).ShouldBe(1);

        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {Line()}
            {Action()}
            UPDATE [CorporateActions] SET [ReceiptLineId] = @line, [ReceivedAmount] = 103 WHERE [Id] = (SELECT [Id] FROM #action);
            {Action(exDate: "2026-12-09")}
            UPDATE [CorporateActions] SET [ReceiptLineId] = @line, [ReceivedAmount] = 103 WHERE [Id] = (SELECT [Id] FROM #action);
            """));
        exception.Number.ShouldBe(2601);
        exception.Message.ShouldContain("UX_CorporateActions_ReceiptLineId");
    }

    [Test]
    public async Task AnAnnouncement_OfADividend_Inserts_AndOneOutsideTheTermChecks_Violates()
    {
        (await RunAsync($"""
            {Announcement("d-1")}
            {Announcement("d-2", kind: "NULL", ratio: "NULL, NULL", terms: "NULL, NULL")}
            SELECT COUNT(*) FROM [CorporateActionAnnouncements] WHERE [OutsideId] IN (N'd-1', N'd-2');
            """)).ShouldBe(2);

        foreach (var (kind, ratio, terms, constraint) in new[]
                 {
                     ("3", "2, 1", "0.72, 0", "CK_CorporateActionAnnouncements_Terms"),
                     ("3", "NULL, NULL", "NULL, 0", "CK_CorporateActionAnnouncements_Terms"),
                     ("0", "NULL, NULL", "NULL, NULL", "CK_CorporateActionAnnouncements_Terms"),
                     ("4", "NULL, NULL", "0.72, 0", "CK_CorporateActionAnnouncements_Kind")
                 })
        {
            var exception = await Should.ThrowAsync<SqlException>(() => RunAsync(
                Announcement("d-3", kind: kind, ratio: ratio, terms: terms)));
            exception.Number.ShouldBe(547);
            exception.Message.ShouldContain(constraint);
        }
    }

    [Test]
    public async Task ADividendEntitlement_Posted_NeedsItsNet_AndARatioEntitlement_NeedsItsQuantities()
    {
        (await RunAsync($"""
            {World()}
            {Action()}
            {Entitlement(status: 1, transaction: "@a_buy", net: true)}
            {Entitlement(account: "@a_bank", status: 2, reason: "2")}
            SELECT COUNT(*) FROM [CorporateActionEntitlements] WHERE [TenantId] = @a_tenant;
            """)).ShouldBe(2);

        var withoutNet = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {Action()}
            {Entitlement(status: 1, transaction: "@a_buy")}
            """));
        withoutNet.Number.ShouldBe(547);
        withoutNet.Message.ShouldContain("CK_CorporateActionEntitlements_Posted");

        var reason = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {Action()}
            {Entitlement(status: 2, reason: "3")}
            """));
        reason.Number.ShouldBe(547);
        reason.Message.ShouldContain("CK_CorporateActionEntitlements_Reason");
    }

    [TestCase("entitled: 0", "CK_CorporateActionEntitlements_EntitledQuantity")]
    [TestCase("gross: -1", "CK_CorporateActionEntitlements_SourceAmounts")]
    [TestCase("reinvests: 1, participating: NULL", "CK_CorporateActionEntitlements_Reinvests")]
    [TestCase("reinvested: 2.68", "CK_CorporateActionEntitlements_ReinvestedQuantity")]
    public async Task ADividendEntitlementOutsideTheChecks_Violates(string change, string constraint)
    {
        var values = change.Split(", ").Select(part => part.Split(": ")).ToDictionary(pair => pair[0], pair => pair[1]);
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {Action()}
            {Entitlement(
                entitled: values.GetValueOrDefault("entitled", "100"),
                gross: values.GetValueOrDefault("gross", "72"),
                reinvests: values.GetValueOrDefault("reinvests", "0"),
                participating: values.GetValueOrDefault("participating", "NULL"),
                reinvested: values.GetValueOrDefault("reinvested", "NULL"))}
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain(constraint);
    }

    [Test]
    public async Task CashLegRoleReinvestment_IsAllowed_AndTheNextValueViolates()
    {
        (await RunAsync($"""
            {World()}
            {CashLeg(role: 3)}
            SELECT COUNT(*) FROM [TransactionCashLegs] WHERE [TransactionId] = @a_buy AND [Role] = 3;
            """)).ShouldBe(1);

        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {CashLeg(role: 4)}
            """));
        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain("CK_TransactionCashLegs_Role");
    }

    [Test]
    public async Task TaxKindForeignWithholding_IsAllowed_AndTheNextValueViolates()
    {
        (await RunAsync($"""
            {World()}
            {CashLeg(role: 2)}
            {TaxDetail(kind: 2)}
            SELECT COUNT(*) FROM [TransactionTaxDetails] WHERE [TransactionId] = @a_buy AND [Kind] = 2;
            """)).ShouldBe(1);

        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {CashLeg(role: 2)}
            {TaxDetail(kind: 3)}
            """));
        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain("CK_TransactionTaxDetails_Kind");
    }

    [Test]
    public async Task ADividendDetail_Inserts_AndOneOutsideTheChecks_Violates()
    {
        (await RunAsync($"""
            {World()}
            {DividendDetail()}
            SELECT COUNT(*) FROM [TransactionDividendDetails] WHERE [TransactionId] = @a_buy;
            """)).ShouldBe(1);

        foreach (var (change, constraint) in new[]
                 {
                     (DividendDetail(quantity: "0"), "CK_TransactionDividendDetails_Quantity"),
                     (DividendDetail(taxSource: "2"), "CK_TransactionDividendDetails_TaxSource"),
                     (DividendDetail(reinvestment: "2.68, NULL"), "CK_TransactionDividendDetails_Reinvestment")
                 })
        {
            var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
                {World()}
                {change}
                """));
            exception.Number.ShouldBe(547);
            exception.Message.ShouldContain(constraint);
        }
    }

    [Test]
    public async Task ReinvestDividends_OnAnExternalAccount_Violates_AndOnACustodialAccountInserts()
    {
        (await RunAsync($"""
            {World()}
            UPDATE [Accounts] SET [CustodyMode] = 1, [DepositReference] = N'D' + LEFT(CONVERT(nvarchar(36), NEWID()), 8), [ReinvestDividends] = 1 WHERE [Id] = @a_brokerage;
            SELECT COUNT(*) FROM [Accounts] WHERE [Id] = @a_brokerage AND [ReinvestDividends] = 1;
            """)).ShouldBe(1);

        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            UPDATE [Accounts] SET [ReinvestDividends] = 1 WHERE [Id] = @a_brokerage;
            """));
        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain("CK_Accounts_ReinvestDividends");
    }

    [Test]
    public async Task ALineMatchedToADividend_Inserts_WithMethod7_AndReason14IsAllowed()
    {
        (await RunAsync($"""
            {World()}
            {Action()}
            {Line(status: 1, method: "7", corporateAction: "(SELECT [Id] FROM #action)")}
            {Line(status: 2, unmatchedReason: "14")}
            SELECT COUNT(*) FROM [BankStatementLines] WHERE [TenantId] = @a_tenant;
            """)).ShouldBe(2);
    }

    [TestCase("status: 1, method: 7, corporateAction: yes, account: yes", "CK_BankStatementLines_Match")]
    [TestCase("status: 2, corporateAction: yes", "CK_BankStatementLines_Match")]
    [TestCase("status: 1, method: 8, corporateAction: yes", "CK_BankStatementLines_MatchMethod")]
    [TestCase("status: 2, unmatchedReason: 15", "CK_BankStatementLines_UnmatchedReason")]
    public async Task ALineOutsideTheDividendChecks_Violates(string change, string constraint)
    {
        var values = change.Split(", ").Select(part => part.Split(": ")).ToDictionary(pair => pair[0], pair => pair[1]);
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {Action()}
            {Line(
                status: int.Parse(values["status"]),
                method: values.GetValueOrDefault("method", "NULL"),
                unmatchedReason: values.GetValueOrDefault("unmatchedReason", "NULL"),
                corporateAction: values.ContainsKey("corporateAction") ? "(SELECT [Id] FROM #action)" : "NULL",
                account: values.ContainsKey("account") ? "@a_brokerage" : "NULL",
                transaction: values.ContainsKey("account") ? "@a_buy" : "NULL")}
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain(constraint);
    }

    [Test]
    public async Task ReconciliationItemKind21_IsAllowed_AndTheNextValueViolates()
    {
        (await RunAsync($"""
            {World()}
            {Run()}
            INSERT INTO [BankReconciliationItems] ([TenantId], [ReconciliationId], [Position], [Kind], [Class], [Amount], [Count])
            VALUES (@a_tenant, @run, 1, 20, 1, 103, 1), (@a_tenant, @run, 2, 21, 2, 0.01, 1);
            SELECT COUNT(*) FROM [BankReconciliationItems] WHERE [ReconciliationId] = @run;
            """)).ShouldBe(2);

        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {Run()}
            INSERT INTO [BankReconciliationItems] ([TenantId], [ReconciliationId], [Position], [Kind], [Class], [Amount], [Count])
            VALUES (@a_tenant, @run, 1, 22, 2, 0.01, 1);
            """));
        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain("CK_BankReconciliationItems_Kind");
    }

    [Test]
    public async Task ADividendAndItsEntitlement_RoundTripThroughEf()
    {
        await using var db = await ScratchSchemaDatabase.CreateAsync("dividends");
        await db.ApplyAllAsync();
        var row = (await db.ReadAsync($"""
            {ScratchSchemaDatabase.TenantRows("a", Guid.NewGuid().ToString("N")[..12])}
            SELECT @a_tenant, @a_instrument, @a_brokerage;
            """)).Single();
        var (tenantId, instrumentId, accountId) = (Convert.ToInt32(row[0]), Convert.ToInt32(row[1]), Convert.ToInt32(row[2]));
        var at = new DateTimeOffset(2026, 11, 24, 3, 41, 0, TimeSpan.Zero);
        var manual = CorporateAction.EnterDividend(
            tenantId, instrumentId, DividendTerms.Create(0.26m, DividendTaxSource.Foreign, null, 15m, null),
            new DateOnly(2026, 12, 8), new DateOnly(2026, 12, 8), new DateOnly(2026, 12, 15), "Quarterly dividend");
        manual.CreatedBy = "test";
        var feed = CorporateAction.AnnounceDividend(
            tenantId, instrumentId, "simulator", "w-CA3", 1,
            DividendTerms.Create(1.00m, DividendTaxSource.Foreign, 0m, 15m, 25m),
            new DateOnly(2026, 11, 26), null, new DateOnly(2026, 11, 30));
        feed.CreatedBy = "test";
        await using (var context = OpenDb(db.ConnectionString))
        {
            context.Set<CorporateAction>().AddRange(manual, feed);
            await context.SaveChangesAsync();
            context.Set<CorporateActionEntitlement>().Add(CorporateActionEntitlement.CreateDividend(
                tenantId, feed.Id, accountId, 100m, new DividendSourceAmounts(100m, 0m, 15m), true, 78.82352941m, at));
            await context.SaveChangesAsync();
        }

        await using (var context = OpenDb(db.ConnectionString))
        {
            var stored = await context.Set<CorporateAction>().AsNoTracking().SingleAsync(action => action.Id == manual.Id);
            (stored.Kind, stored.RatioNew, stored.RatioHeld)
                .ShouldBe((CorporateActionKind.Dividend, (int?)null, (int?)null));
            stored.Terms.ShouldBe(new DividendTerms(0.26m, DividendTaxSource.Foreign, 0m, 15m, null));
            var storedFeed = await context.Set<CorporateAction>().AsNoTracking().SingleAsync(action => action.Id == feed.Id);
            storedFeed.Terms!.ReinvestmentPrice.ShouldBe(25m);

            var entitlement = await context.Set<CorporateActionEntitlement>().AsNoTracking()
                .SingleAsync(item => item.CorporateActionId == feed.Id);
            (entitlement.Status, entitlement.EntitledQuantity, entitlement.GrossAmount, entitlement.ImputationCredit,
                    entitlement.ForeignTax, entitlement.Reinvests, entitlement.ParticipatingQuantity)
                .ShouldBe((EntitlementStatus.Pending, (decimal?)100m, (decimal?)100m, (decimal?)0m, (decimal?)15m, true,
                    (decimal?)78.82352941m));
        }
    }

    [Test]
    public async Task ADividendTransaction_WithItsTaxDetailsReinvestmentAndDetail_RoundTripsThroughEf()
    {
        await using var db = await ScratchSchemaDatabase.CreateAsync("dividendposting");
        await db.ApplyAllAsync();
        var row = (await db.ReadAsync($"""
            {ScratchSchemaDatabase.TenantRows("a", Guid.NewGuid().ToString("N")[..12])}
            SELECT @a_tenant, @a_instrument, @a_bank;
            """)).Single();
        var (tenantId, instrumentId, accountId) = (Convert.ToInt32(row[0]), Convert.ToInt32(row[1]), Convert.ToInt32(row[2]));
        var terms = DividendTerms.Create(1.00m, DividendTaxSource.Foreign, null, 15m, 25m);
        var posting = Transaction.PostDividend(
            tenantId, accountId, "USD", 2, instrumentId, "VTI", "USD", terms, new DateOnly(2026, 11, 26), null,
            new DateOnly(2026, 11, 30), 100m, terms.SourceAmounts(100m, 2), new DividendTax(TaxKind.Rwt, 33m), 2.68m, null,
            new DateTimeOffset(2026, 11, 24, 4, 10, 31, TimeSpan.Zero), TransactionSource.CorporateAction(Guid.NewGuid()));
        posting.CreatedBy = "test";
        posting.CashLegs.ForEach(leg => leg.CreatedBy = "test");
        posting.SecurityLegs.ForEach(leg => leg.CreatedBy = "test");
        await using (var context = OpenDb(db.ConnectionString))
        {
            context.Set<Transaction>().Add(posting);
            await context.SaveChangesAsync();
        }

        await using (var context = OpenDb(db.ConnectionString))
        {
            var stored = await context.Set<Transaction>().AsNoTracking()
                .Include(transaction => transaction.CashLegs).ThenInclude(leg => leg.TaxDetail)
                .Include(transaction => transaction.SecurityLegs)
                .Include(transaction => transaction.DividendDetail)
                .SingleAsync(transaction => transaction.Id == posting.Id);
            stored.Type.ShouldBe(TransactionType.Dividend);
            stored.CashLegs.Select(leg => (leg.Role, leg.Amount, leg.TaxDetail?.Kind)).ShouldBe(
            [
                (CashLegRole.Principal, 100m, null),
                (CashLegRole.Tax, -15m, TaxKind.ForeignWithholding),
                (CashLegRole.Tax, -18m, TaxKind.Rwt),
                (CashLegRole.Reinvestment, -67m, null)
            ], ignoreOrder: true);
            stored.SecurityLegs.Single().Quantity.ShouldBe(2.68m);
            (stored.DividendDetail!.Quantity, stored.DividendDetail.AmountPerShare, stored.DividendDetail.Currency,
                    stored.DividendDetail.TaxSource, stored.DividendDetail.ReinvestedQuantity, stored.DividendDetail.ReinvestmentPrice)
                .ShouldBe((100m, 1.00m, "USD", DividendTaxSource.Foreign, (decimal?)2.68m, (decimal?)25m));
        }
    }

    /// <summary>A tenant (<c>@a_*</c> from <see cref="ScratchSchemaDatabase.TenantRows"/>) with a client money account
    /// <c>@cma</c>.</summary>
    private static string World() => $"""
        {ScratchSchemaDatabase.TenantRows("a", Guid.NewGuid().ToString("N")[..12])}
        CREATE TABLE #action ([Id] int);
        INSERT INTO [ClientMoneyAccounts]
            ([PublicId], [TenantId], [Currency], [BankName], [AccountName], [AccountNumber], [IsActive], [Created], [CreatedBy], [LastModified], [BankConnection])
        VALUES (NEWID(), @a_tenant, 'USD', N'Simulator Bank', N'Trust', N'N-{Guid.NewGuid().ToString("N")[..12]}', 1,
                SYSDATETIMEOFFSET(), N'test', SYSDATETIMEOFFSET(), N'simulator');
        DECLARE @cma int = SCOPE_IDENTITY();
        DECLARE @line int;
        """;

    /// <summary>A corporate action on <c>@a_instrument</c>: a Manual dividend unless told otherwise; its Id is the one
    /// row of <c>#action</c>. Terms are amount, tax source, credit, foreign rate, plan price.</summary>
    private static string Action(
        int kind = 3,
        string ratio = "NULL, NULL",
        string terms = "0.72, 0, 0.28, 0, NULL",
        string exDate = "2026-12-01") => $"""
        INSERT INTO [CorporateActions]
            ([PublicId], [TenantId], [InstrumentId], [Kind], [RatioNew], [RatioHeld], [AmountPerShare], [TaxSource],
             [ImputationCreditPerShare], [ForeignTaxRatePercent], [ReinvestmentPrice], [ExDate], [Origin], [Status],
             [Created], [CreatedBy])
        VALUES (NEWID(), @a_tenant, @a_instrument, {kind}, {ratio}, {terms}, '{exDate}', 0, 0, SYSDATETIMEOFFSET(), N'test');
        DELETE FROM #action; INSERT INTO #action VALUES (SCOPE_IDENTITY());
        """;

    private static string Announcement(
        string outsideId,
        string kind = "3",
        string ratio = "NULL, NULL",
        string terms = "0.72, 0") => $"""
        INSERT INTO [CorporateActionAnnouncements]
            ([PublicId], [Connection], [OutsideId], [PartyActionId], [Version], [PartyStatus], [Symbol], [Currency], [Kind],
             [RatioNew], [RatioHeld], [AmountPerShare], [TaxSource], [ExDate], [ReceivedAt], [Status])
        VALUES (NEWID(), N'simulator', N'{outsideId}', N'w-CA3', 1, 0, N'ACME.SIM', 'USD', {kind},
                {ratio}, {terms}, '2026-12-01', SYSDATETIMEOFFSET(), 0);
        """;

    private static string Entitlement(
        string account = "@a_brokerage",
        int status = 0,
        string reason = "NULL",
        string transaction = "NULL",
        bool net = false,
        string entitled = "100",
        string gross = "72",
        string reinvests = "0",
        string participating = "NULL",
        string reinvested = "NULL") => $"""
        INSERT INTO [CorporateActionEntitlements]
            ([TenantId], [CorporateActionId], [AccountId], [Status], [Reason], [TransactionId], [PostedAt],
             [EntitledQuantity], [GrossAmount], [ImputationCredit], [ForeignTax], [Reinvests], [ParticipatingQuantity],
             [ReinvestedQuantity], [NetAmount], [NetCurrency])
        SELECT @a_tenant, [Id], {account}, {status}, {reason}, {transaction},
               {(status == 0 ? "NULL" : "SYSDATETIMEOFFSET()")},
               {entitled}, {gross}, 28, 0, {reinvests}, {participating}, {reinvested},
               {(net ? "67" : "NULL")}, {(net ? "'USD'" : "NULL")}
        FROM #action;
        """;

    private static string CashLeg(int role) => $"""
        INSERT INTO [TransactionCashLegs] ([TenantId], [TransactionId], [AccountId], [Role], [Amount], [Currency], [Created], [CreatedBy])
        VALUES (@a_tenant, @a_buy, @a_brokerage, {role}, -5, 'NZD', SYSDATETIMEOFFSET(), N'test');
        DECLARE @leg int = SCOPE_IDENTITY();
        """;

    private static string TaxDetail(int kind) => $"""
        INSERT INTO [TransactionTaxDetails] ([CashLegId], [TenantId], [TransactionId], [Kind], [RatePercent], [BasisAmount], [TaxAmount], [Currency])
        VALUES (@leg, @a_tenant, @a_buy, {kind}, 15, 100, 15, 'NZD');
        """;

    private static string DividendDetail(
        string quantity = "100",
        string taxSource = "1",
        string reinvestment = "2.68, 25") => $"""
        INSERT INTO [TransactionDividendDetails]
            ([TransactionId], [TenantId], [InstrumentId], [Quantity], [AmountPerShare], [Currency], [TaxSource],
             [ImputationCredit], [ReinvestedQuantity], [ReinvestmentPrice], [ExDate], [RecordDate], [PaymentDate])
        VALUES (@a_buy, @a_tenant, @a_instrument, {quantity}, 1.00, 'USD', {taxSource}, 0, {reinvestment},
                '2026-11-26', NULL, '2026-11-30');
        """;

    private static string Line(
        int status = 2,
        string method = "NULL",
        string unmatchedReason = "NULL",
        string corporateAction = "NULL",
        string account = "NULL",
        string transaction = "NULL") => $"""
        INSERT INTO [BankStatementLines]
            ([PublicId], [TenantId], [ClientMoneyAccountId], [Connection], [OutsideId], [ValueDate], [Direction], [Amount], [Currency],
             [ReceivedAt], [Status], [UnmatchedReason], [MatchMethod], [MatchedAt], [MatchedAccountId], [MatchedTransactionId],
             [CorporateActionId], [Created], [CreatedBy])
        VALUES (NEWID(), @a_tenant, @cma, N'simulator', CONVERT(nvarchar(100), NEWID()), '2026-11-30', 0, 103, 'USD',
                SYSDATETIMEOFFSET(), {status}, {(status == 2 && unmatchedReason == "NULL" ? "0" : unmatchedReason)}, {method},
                {(method == "NULL" ? "NULL" : "SYSDATETIMEOFFSET()")}, {account}, {transaction}, {corporateAction},
                SYSDATETIMEOFFSET(), N'test');
        SET @line = SCOPE_IDENTITY();
        """;

    private static string Run() => """
        INSERT INTO [BankReconciliations]
            ([PublicId], [TenantId], [ClientMoneyAccountId], [Currency], [Trigger], [RanAt], [RanByUserId], [Status],
             [BankBalance], [BalanceAt], [OpeningBalance], [StatementLinesTotal], [Difference], [CustodialCash],
             [BreakCount], [BreakTotal], [TimingCount], [TimingTotal], [KnownCount], [KnownTotal], [ReservedCash],
             [PaymentsInFlightCount], [PaymentsInFlightAmount], [Created], [CreatedBy])
        VALUES (NEWID(), @a_tenant, @cma, 'USD', 0, SYSDATETIMEOFFSET(), NULL, 0,
                103.01, SYSDATETIMEOFFSET(), 0, 103.01, 103.01, 0, 0, 0, 1, 103, 1, 0.01, 0, 0, 0, SYSDATETIMEOFFSET(), N'test');
        DECLARE @run int = SCOPE_IDENTITY();
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
