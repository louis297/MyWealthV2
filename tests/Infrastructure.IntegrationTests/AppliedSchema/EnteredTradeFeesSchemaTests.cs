using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyWealthV2.Domain.Common;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.ValueObjects;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Data.Schema;
using MyWealthV2.Infrastructure.Identity;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

/// <summary>External caller numbers §6: a <c>Brokerage</c> fee detail may have no schedule (script 0034).</summary>
public class EnteredTradeFeesSchemaTests
{
    private const string EnteredTradeFeesScript = "0034_entered_trade_fees.sql";

    private const string ScheduledDetail = """
        INSERT INTO [TransactionFeeDetails] ([CashLegId], [TenantId], [TransactionId], [Kind], [TradeFeeScheduleId], [BasisAmount], [FeeAmount], [Currency])
        VALUES (@a_feeleg, @a_tenant, @a_buy, 0, @a_feeschedule, 1250, 10, 'USD');
        """;

    private const string DropSchedule = "UPDATE [TransactionFeeDetails] SET [TradeFeeScheduleId] = NULL;";

    [Test]
    public void SchemaDirectory_Includes0034EnteredTradeFeesScript()
    {
        new FileSchemaScriptSource(ScratchSchemaDatabase.SchemaDirectory()).GetScripts()
            .Select(script => script.Name)
            .ShouldContain(EnteredTradeFeesScript);
    }

    [Test]
    public async Task Apply0034_KeepsExistingFeeDetailsAndAllowsABrokerageFeeWithoutSchedule()
    {
        await using var db = await ScratchSchemaDatabase.CreateAsync("EnteredTradeFees");
        await db.ApplyBeforeAsync(EnteredTradeFeesScript);
        await db.ExecuteAsync(
            ScratchSchemaDatabase.TenantRows("a", Guid.NewGuid().ToString("N")[..12])
            + ScratchSchemaDatabase.FeeRows("a")
            + ScheduledDetail);
        const string read = "SELECT [CashLegId], [Kind], [TradeFeeScheduleId], [BasisAmount], [FeeAmount] FROM [TransactionFeeDetails]";
        var before = await db.ReadAsync(read);

        // 547: the CHECK of 0030 refuses a Brokerage fee without a schedule.
        (await Should.ThrowAsync<SqlException>(() => db.ExecuteAsync(DropSchedule))).Number.ShouldBe(547);

        await db.ApplyAllAsync();

        var after = await db.ReadAsync(read);
        before.Count.ShouldBe(1);
        after.Single().ShouldBe(before.Single());
        (await db.ReadAsync($"SELECT 1 FROM [SchemaVersions] WHERE [ScriptName] = N'{EnteredTradeFeesScript}'"))
            .Count.ShouldBe(1);

        await db.ExecuteAsync(DropSchedule);
        (await db.ReadAsync("SELECT [TradeFeeScheduleId] FROM [TransactionFeeDetails]"))
            .Single()[0].ShouldBe(DBNull.Value);
    }

    /// <summary>External caller numbers R9, R10: an entered fee and a caller rate survive a save and a reload.</summary>
    [Test]
    public async Task Save_BuyWithAnEnteredFeeAndACallerRate_ReloadsBothDetails()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(SchemaDatabaseSetup.ConnectionString)
            .Options;
        Guid publicId;
        await using (var db = new ApplicationDbContext(options))
        {
            var (tenant, _) = await TwoTenantFixture.InsertAsync(db);
            var suffix = Guid.NewGuid().ToString("N")[..8];
            var adviser = User.Create(
                UserRole.Adviser, "Adviser", $"adviser-{suffix}@example", await IdentityAsync(db, tenant), tenant.Id, null);
            db.DomainUsers.Add(adviser);
            await SaveAsync(db);
            var customer = User.Create(
                UserRole.Customer, "Customer", $"customer-{suffix}@example", await IdentityAsync(db, tenant), tenant.Id, adviser.Id);
            db.DomainUsers.Add(customer);
            await SaveAsync(db);
            var nzd = Currency.Create("NZD", "New Zealand Dollar", 2);
            var usd = Currency.Create("USD", "US Dollar", 2);
            var account = Account.Create(tenant.Id, customer.Id, "Held elsewhere", AccountType.Brokerage, CustodyMode.External, nzd);
            var instrument = Instrument.Create(tenant.Id, $"I{suffix}"[..8].ToUpperInvariant(), "Instrument", usd);
            db.Accounts.Add(account);
            db.Instruments.Add(instrument);
            await SaveAsync(db);
            var bookedAt = new DateTimeOffset(2026, 11, 17, 22, 30, 0, TimeSpan.Zero);
            var buy = Transaction.PostBuy(
                tenant.Id, account.Id, AccountType.Brokerage, "NZD", instrument.Id, 10m,
                Money.Create(180.00m, "USD"), 1.69147m, bookedAt, null, null, 10000m,
                enteredFee: 9.95m,
                appliedRate: AppliedFxRate.Create("USD", "NZD", 1.69147m, new DateOnly(2026, 11, 17), AppliedFxRate.CallerSource));
            db.Transactions.Add(buy);
            await SaveAsync(db);
            publicId = buy.PublicId;
        }

        await using (var db = new ApplicationDbContext(options))
        {
            var loaded = await db.Transactions.IgnoreQueryFilters().AsNoTracking()
                .Include(transaction => transaction.CashLegs).ThenInclude(leg => leg.FeeDetail)
                .Include(transaction => transaction.FxDetail)
                .SingleAsync(transaction => transaction.PublicId == publicId);
            var fee = loaded.CashLegs.Single(leg => leg.Role == CashLegRole.Fee);
            fee.Amount.ShouldBe(-16.8301m);
            var detail = fee.FeeDetail.ShouldNotBeNull();
            detail.TradeFeeScheduleId.ShouldBeNull();
            (detail.Kind, detail.BasisAmount, detail.FeeAmount, detail.Currency)
                .ShouldBe((FeeKind.Brokerage, 1800.0000m, 9.95m, "USD"));
            var fx = loaded.FxDetail.ShouldNotBeNull();
            (fx.BaseCurrency.Trim(), fx.QuoteCurrency.Trim(), fx.Rate, fx.RateAsOfDate, fx.RateSource)
                .ShouldBe(("USD", "NZD", 1.69147m, new DateOnly(2026, 11, 17), "Caller"));
        }
    }

    private static async Task<string> IdentityAsync(ApplicationDbContext db, Tenant tenant)
    {
        var identity = new ApplicationUser
        {
            UserName = Guid.NewGuid().ToString(),
            Email = $"{Guid.NewGuid():N}@example",
            EmailConfirmed = true,
            TenantId = tenant.Id
        };
        db.Users.Add(identity);
        await db.SaveChangesAsync();
        return identity.Id;
    }

    private static async Task SaveAsync(ApplicationDbContext db)
    {
        foreach (var entry in db.ChangeTracker.Entries<BaseAuditableEntity>()
                     .Where(entry => entry.State == EntityState.Added))
        {
            entry.Entity.CreatedBy ??= "test";
        }

        await db.SaveChangesAsync();
    }
}
