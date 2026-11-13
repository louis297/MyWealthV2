using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MyWealthV2.Domain.Common;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.ValueObjects;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Data.Entities;
using MyWealthV2.Infrastructure.Identity;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

public class TenantQueryFilterTests
{
    private static readonly Type[] FilteredTypes =
    [
        typeof(User),
        typeof(Instrument),
        typeof(Account),
        typeof(Transaction),
        typeof(Holding),
        typeof(IdempotencyRecord),
        typeof(TenantReport),
        typeof(ReportExecution),
        typeof(ReportSchedule),
        typeof(UserToken),
        typeof(TransactionCashLeg),
        typeof(TransactionSecurityLeg),
        typeof(TransactionFxDetail),
        typeof(PortfolioLine)
    ];

    private static readonly Type[] UnfilteredTypes =
    [
        typeof(Tenant),
        typeof(Currency),
        typeof(Report),
        typeof(ApplicationUser)
    ];

    [Test]
    public async Task EngagedTenant_QueryWithoutWhere_SeesOnlyThatTenant()
    {
        var accessor = new TenantIdAccessor();
        await using var db = OpenDb(accessor);
        var (tenantA, tenantB) = await TwoTenantFixture.InsertAsync(db);
        var (adminA, adminB) = await TwoTenantFixture.InsertTenantAdminsAsync(db, tenantA, tenantB);
        var nzd = Currency.Create("NZD", "New Zealand Dollar", 2);
        var instrumentA = Instrument.Create(tenantA.Id, $"AAA{Guid.NewGuid():N}"[..8], "Alpha", nzd);
        var instrumentB = Instrument.Create(tenantB.Id, $"BBB{Guid.NewGuid():N}"[..8], "Beta", nzd);
        instrumentA.CreatedBy = "test";
        instrumentB.CreatedBy = "test";
        db.Instruments.AddRange(instrumentA, instrumentB);
        await db.SaveChangesAsync();

        accessor.Engage(tenantA.Id);

        var people = await db.DomainUsers.AsNoTracking()
            .Where(person => person.PublicId == adminA.PublicId || person.PublicId == adminB.PublicId)
            .Select(person => person.PublicId)
            .ToListAsync();
        var instruments = await db.Instruments.AsNoTracking()
            .Where(row => row.PublicId == instrumentA.PublicId || row.PublicId == instrumentB.PublicId)
            .Select(row => row.PublicId)
            .ToListAsync();

        people.ShouldBe([adminA.PublicId]);
        instruments.ShouldBe([instrumentA.PublicId]);
    }

    [Test]
    public async Task EngagedTenant_ChildTablesWithoutWhere_SeeOnlyThatTenant()
    {
        var accessor = new TenantIdAccessor();
        await using var db = OpenDb(accessor);
        var (tenantA, tenantB) = await TwoTenantFixture.InsertAsync(db);
        var rowsA = await InsertChildRowsAsync(db, tenantA);
        var rowsB = await InsertChildRowsAsync(db, tenantB);
        int[] transactionIds = [rowsA.BuyId, rowsA.FxId, rowsB.BuyId, rowsB.FxId];
        int[] portfolioIds = [rowsA.PortfolioId, rowsB.PortfolioId];

        async Task<(int[] Cash, int[] Security, int[] Fx, int[] Lines)> ReadAsync() =>
        (
            (await db.TransactionCashLegs.AsNoTracking()
                .Where(leg => transactionIds.Contains(leg.TransactionId))
                .Select(leg => leg.TransactionId).Distinct().ToListAsync()).Order().ToArray(),
            (await db.TransactionSecurityLegs.AsNoTracking()
                .Where(leg => transactionIds.Contains(leg.TransactionId))
                .Select(leg => leg.TransactionId).Distinct().ToListAsync()).Order().ToArray(),
            (await db.Set<TransactionFxDetail>().AsNoTracking()
                .Where(detail => transactionIds.Contains(detail.TransactionId))
                .Select(detail => detail.TransactionId).ToListAsync()).Order().ToArray(),
            (await db.PortfolioLines.AsNoTracking()
                .Where(line => portfolioIds.Contains(line.PortfolioId))
                .Select(line => line.PortfolioId).Distinct().ToListAsync()).Order().ToArray()
        );

        var disengaged = await ReadAsync();
        accessor.Engage(tenantA.Id);
        var engaged = await ReadAsync();

        disengaged.Cash.ShouldBe(new[] { rowsA.BuyId, rowsA.FxId, rowsB.BuyId, rowsB.FxId }.Order().ToArray());
        disengaged.Security.ShouldBe(new[] { rowsA.BuyId, rowsB.BuyId }.Order().ToArray());
        disengaged.Fx.ShouldBe(new[] { rowsA.FxId, rowsB.FxId }.Order().ToArray());
        disengaged.Lines.ShouldBe(new[] { rowsA.PortfolioId, rowsB.PortfolioId }.Order().ToArray());

        engaged.Cash.ShouldBe(new[] { rowsA.BuyId, rowsA.FxId }.Order().ToArray());
        engaged.Security.ShouldBe([rowsA.BuyId]);
        engaged.Fx.ShouldBe([rowsA.FxId]);
        engaged.Lines.ShouldBe([rowsA.PortfolioId]);
    }

    [Test]
    public void Model_FiltersTenantScopedTypes_AndLeavesPlatformAndIdentityTypes()
    {
        using var db = OpenDb(new TenantIdAccessor());

        foreach (var type in FilteredTypes)
        {
            db.Model.FindEntityType(type)!.GetDeclaredQueryFilters().ShouldNotBeEmpty($"expected a filter on {type.Name}");
        }

        foreach (var type in UnfilteredTypes)
        {
            db.Model.FindEntityType(type)!.GetDeclaredQueryFilters().ShouldBeEmpty($"expected no filter on {type.Name}");
        }
    }

    /// <summary>A Buy (cash + security leg), an FxConversion (cash legs + FX detail) and a Portfolio with lines.</summary>
    private static async Task<(int BuyId, int FxId, int PortfolioId)> InsertChildRowsAsync(
        ApplicationDbContext db,
        Tenant tenant)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var adviser = User.Create(UserRole.Adviser, "Adviser", $"adviser-{suffix}@example", await IdentityAsync(db, tenant), tenant.Id, null);
        db.DomainUsers.Add(adviser);
        await db.SaveChangesAsync();
        var customer = User.Create(UserRole.Customer, "Customer", $"customer-{suffix}@example", await IdentityAsync(db, tenant), tenant.Id, adviser.Id);
        db.DomainUsers.Add(customer);
        await db.SaveChangesAsync();

        var nzd = Currency.Create("NZD", "New Zealand Dollar", 2);
        var usd = Currency.Create("USD", "US Dollar", 2);
        var brokerage = Account.Create(tenant.Id, customer.Id, "Brokerage", AccountType.Brokerage, nzd);
        var usdBank = Account.Create(tenant.Id, customer.Id, "USD Bank", AccountType.Bank, usd);
        var instrument = Instrument.Create(tenant.Id, $"I{suffix}"[..8].ToUpperInvariant(), "Instrument", usd);
        db.Accounts.AddRange(brokerage, usdBank);
        db.Instruments.Add(instrument);
        await SaveAsync(db);

        var buy = Transaction.PostBuy(
            tenant.Id, brokerage.Id, AccountType.Brokerage, "NZD", instrument.Id, 1m,
            Money.Create(10m, "USD"), 2m, DateTimeOffset.UtcNow, null, null, 1000m);
        var fx = Transaction.PostFxConversion(
            tenant.Id, brokerage.Id, "NZD", usdBank.Id, "USD",
            AppliedFxRate.Create("NZD", "USD", 0.5m, new DateOnly(2026, 11, 12), "Frankfurter"),
            Money.Create(100m, "NZD"), 2, DateTimeOffset.UtcNow, null, null, 1000m);
        var portfolio = Portfolio.Create(tenant.Id, $"Portfolio {suffix}", nzd, [(instrument.Id, 100m)]);
        db.Transactions.AddRange(buy, fx);
        db.Portfolios.Add(portfolio);
        await SaveAsync(db);

        return (buy.Id, fx.Id, portfolio.Id);
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

    private static ApplicationDbContext OpenDb(TenantIdAccessor accessor)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(SchemaDatabaseSetup.ConnectionString)
            .ConfigureWarnings(warnings => warnings.Throw(
                CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning))
            .Options;
        return new ApplicationDbContext(options, accessor);
    }
}
