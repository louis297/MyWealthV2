using Microsoft.EntityFrameworkCore;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Identity;
using MyWealthV2.Infrastructure.MarketData;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

public class TestSeedAccountsTests
{
    [Test]
    public async Task Seed_IsIdempotentForDemoAccountsWhenCustomerExists()
    {
        await using var db = OpenDb();
        var tenant = await EnsureDemoTenantAsync(db);
        var adviser = await EnsurePersonAsync(
            db, tenant, UserRole.Adviser, "Demo Adviser", DevelopmentIdentitySeeder.AdviserEmail);
        await EnsurePersonAsync(
            db, tenant, UserRole.Customer, "Demo Customer", "customer@localhost", adviser.Id);
        var prices = new MarketDataMock();

        await TestSeed.SeedAsync(db, prices, CancellationToken.None);
        var first = await LoadSeededAsync(db);
        await TestSeed.SeedAsync(db, prices, CancellationToken.None);
        var second = await LoadSeededAsync(db);

        first.Select(row => row.Name).ShouldBe([
            "Demo brokerage",
            "Demo cash tin",
            "Demo closed other",
            "Demo everyday bank"
        ]);
        second.Select(row => row.PublicId).ShouldBe(first.Select(row => row.PublicId));

        var bank = first.Single(row => row.Name == "Demo everyday bank");
        bank.Type.ShouldBe(AccountType.Bank);
        bank.Currency.ShouldBe("NZD");
        bank.Status.ShouldBe(AccountStatus.Open);
        bank.IsActive.ShouldBeTrue();

        var brokerage = first.Single(row => row.Name == "Demo brokerage");
        brokerage.Type.ShouldBe(AccountType.Brokerage);
        brokerage.Currency.ShouldBe("USD");
        brokerage.Status.ShouldBe(AccountStatus.Open);

        var cash = first.Single(row => row.Name == "Demo cash tin");
        cash.Type.ShouldBe(AccountType.Cash);
        cash.Currency.ShouldBe("NZD");
        cash.Status.ShouldBe(AccountStatus.Open);

        var closed = first.Single(row => row.Name == "Demo closed other");
        closed.Type.ShouldBe(AccountType.Other);
        closed.Currency.ShouldBe("NZD");
        closed.Status.ShouldBe(AccountStatus.Closed);
        closed.IsActive.ShouldBeFalse();
    }

    [Test]
    public async Task Seed_AddsOneTransferInOnTheDemoEverydayBank()
    {
        await using var db = OpenDb();
        var tenant = await EnsureDemoTenantAsync(db);
        var adviser = await EnsurePersonAsync(
            db, tenant, UserRole.Adviser, "Demo Adviser", DevelopmentIdentitySeeder.AdviserEmail);
        await EnsurePersonAsync(
            db, tenant, UserRole.Customer, "Demo Customer", "customer@localhost", adviser.Id);
        var prices = new MarketDataMock();

        await TestSeed.SeedAsync(db, prices, CancellationToken.None);
        await TestSeed.SeedAsync(db, prices, CancellationToken.None);

        var bank = await db.Accounts.SingleAsync(row => row.TenantId == tenant.Id && row.Name == "Demo everyday bank");
        var rows = await (
            from transaction in db.Transactions
            join leg in db.TransactionCashLegs on transaction.Id equals leg.TransactionId
            where transaction.AccountId == bank.Id
                  && transaction.Reference == "TestSeed-DemoEverydayBank-OpeningTransfer"
            select new { transaction.Type, transaction.CreatedBy, leg.Amount, leg.Currency }).ToListAsync();

        rows.Count.ShouldBe(1);
        rows[0].Type.ShouldBe(TransactionType.TransferIn);
        rows[0].Amount.ShouldBe(100m);
        rows[0].Currency.ShouldBe("NZD");
        rows[0].CreatedBy.ShouldBe(TestSeed.CreatedBy);
    }

    [Test]
    public async Task Seed_AddsOneOpeningHoldingsOnTheDemoBrokerageForVti()
    {
        await using var db = OpenDb();
        var tenant = await EnsureDemoTenantAsync(db);
        var adviser = await EnsurePersonAsync(
            db, tenant, UserRole.Adviser, "Demo Adviser", DevelopmentIdentitySeeder.AdviserEmail);
        await EnsurePersonAsync(
            db, tenant, UserRole.Customer, "Demo Customer", "customer@localhost", adviser.Id);
        var prices = new MarketDataMock();

        await TestSeed.SeedAsync(db, prices, CancellationToken.None);
        var first = await LoadOpeningAsync(db, tenant.Id);
        await TestSeed.SeedAsync(db, prices, CancellationToken.None);
        var second = await LoadOpeningAsync(db, tenant.Id);

        first.TransactionPublicId.ShouldNotBe(Guid.Empty);
        second.TransactionPublicId.ShouldBe(first.TransactionPublicId);
        second.HoldingPublicId.ShouldBe(first.HoldingPublicId);

        first.Type.ShouldBe(TransactionType.OpeningHoldings);
        first.Symbol.ShouldBe("VTI");
        first.Quantity.ShouldBe(10m);
        first.CostAmount.ShouldBe(2500.00m);
        first.CostCurrency.Trim().ShouldBe("USD");
        first.HoldingQuantity.ShouldBe(32m);
        first.HoldingCostAmount.ShouldBe(3800.00m);
        first.HoldingCostCurrency.Trim().ShouldBe("USD");
        first.CreatedBy.ShouldBe(TestSeed.CreatedBy);
        first.CashLegCount.ShouldBe(0);
    }

    [Test]
    public async Task Seed_AddsFundingBuyAndStockInOnTheDemoBrokerage()
    {
        await using var db = OpenDb();
        var tenant = await EnsureDemoTenantAsync(db);
        var adviser = await EnsurePersonAsync(
            db, tenant, UserRole.Adviser, "Demo Adviser", DevelopmentIdentitySeeder.AdviserEmail);
        await EnsurePersonAsync(
            db, tenant, UserRole.Customer, "Demo Customer", "customer@localhost", adviser.Id);
        var prices = new MarketDataMock();

        await TestSeed.SeedAsync(db, prices, CancellationToken.None);
        await TestSeed.SeedAsync(db, prices, CancellationToken.None);

        var account = await db.Accounts.SingleAsync(row => row.TenantId == tenant.Id && row.Name == "Demo brokerage");
        var vti = await db.Instruments.SingleAsync(row => row.TenantId == tenant.Id && row.Symbol == "VTI");

        var funding = await LoadCashAsync(db, account.Id, "TestSeed-DemoBrokerage-Funding");
        funding.Count.ShouldBe(1);
        funding[0].Type.ShouldBe(TransactionType.TransferIn);
        funding[0].Amount.ShouldBe(1000m);
        funding[0].Currency.Trim().ShouldBe("USD");

        var buy = await LoadSecurityAsync(db, account.Id, "TestSeed-DemoBrokerage-Buy");
        buy.Count.ShouldBe(1);
        buy[0].Type.ShouldBe(TransactionType.Buy);
        buy[0].CashAmount.ShouldBe(-500m);
        buy[0].Quantity.ShouldBe(2m);
        buy[0].CostAmount.ShouldBe(500m);

        var stockIn = await LoadSecurityAsync(db, account.Id, "TestSeed-DemoBrokerage-StockIn");
        stockIn.Count.ShouldBe(1);
        stockIn[0].Type.ShouldBe(TransactionType.StockIn);
        stockIn[0].CashCount.ShouldBe(0);
        stockIn[0].Quantity.ShouldBe(4m);
        stockIn[0].CostAmount.ShouldBe(800m);

        var holding = await db.Holdings.SingleAsync(row => row.AccountId == account.Id && row.InstrumentId == vti.Id);
        holding.Quantity.ShouldBe(16m);
        holding.CostAmount.ShouldBe(3800m);
    }

    [Test]
    public async Task Seed_AddsOneSplitOnTheDemoBrokerageHolding()
    {
        await using var db = OpenDb();
        var tenant = await EnsureDemoTenantAsync(db);
        var adviser = await EnsurePersonAsync(
            db, tenant, UserRole.Adviser, "Demo Adviser", DevelopmentIdentitySeeder.AdviserEmail);
        await EnsurePersonAsync(
            db, tenant, UserRole.Customer, "Demo Customer", "customer@localhost", adviser.Id);
        var prices = new MarketDataMock();

        await TestSeed.SeedAsync(db, prices, CancellationToken.None);
        var account = await db.Accounts.SingleAsync(row => row.TenantId == tenant.Id && row.Name == "Demo brokerage");
        var first = await LoadSecurityAsync(db, account.Id, "TestSeed-DemoBrokerage-Split");
        var firstId = await db.Transactions
            .Where(row => row.AccountId == account.Id && row.Reference == "TestSeed-DemoBrokerage-Split")
            .Select(row => row.PublicId)
            .SingleAsync();

        await TestSeed.SeedAsync(db, prices, CancellationToken.None);
        var second = await LoadSecurityAsync(db, account.Id, "TestSeed-DemoBrokerage-Split");
        var secondId = await db.Transactions
            .Where(row => row.AccountId == account.Id && row.Reference == "TestSeed-DemoBrokerage-Split")
            .Select(row => row.PublicId)
            .SingleAsync();

        first.Count.ShouldBe(1);
        first[0].Type.ShouldBe(TransactionType.Split);
        first[0].CashCount.ShouldBe(0);
        first[0].Quantity.ShouldBe(16m);
        first[0].CostAmount.ShouldBe(0m);
        second.Count.ShouldBe(1);
        secondId.ShouldBe(firstId);

        var vti = await db.Instruments.SingleAsync(row => row.TenantId == tenant.Id && row.Symbol == "VTI");
        var holding = await db.Holdings.SingleAsync(row => row.AccountId == account.Id && row.InstrumentId == vti.Id);
        holding.Quantity.ShouldBe(32m);
        holding.CostAmount.ShouldBe(3800m);
    }

    private static async Task<List<(TransactionType Type, decimal Amount, string Currency)>> LoadCashAsync(
        ApplicationDbContext db,
        int accountId,
        string reference)
    {
        var rows = await (
            from transaction in db.Transactions
            join leg in db.TransactionCashLegs on transaction.Id equals leg.TransactionId
            where transaction.AccountId == accountId && transaction.Reference == reference
            select new { transaction.Type, leg.Amount, leg.Currency }).ToListAsync();
        return rows.Select(row => (row.Type, row.Amount, row.Currency)).ToList();
    }

    private static async Task<List<(TransactionType Type, decimal? CashAmount, int CashCount, decimal Quantity, decimal CostAmount)>> LoadSecurityAsync(
        ApplicationDbContext db,
        int accountId,
        string reference)
    {
        var transactions = await db.Transactions
            .Where(row => row.AccountId == accountId && row.Reference == reference)
            .ToListAsync();
        var result = new List<(TransactionType, decimal?, int, decimal, decimal)>();
        foreach (var transaction in transactions)
        {
            var cash = await db.TransactionCashLegs.Where(row => row.TransactionId == transaction.Id).ToListAsync();
            var security = await db.TransactionSecurityLegs.SingleAsync(row => row.TransactionId == transaction.Id);
            result.Add((transaction.Type, cash.SingleOrDefault()?.Amount, cash.Count, security.Quantity, security.CostAmount));
        }

        return result;
    }

    private static async Task<SeededOpening> LoadOpeningAsync(ApplicationDbContext db, int tenantId)
    {
        var account = await db.Accounts.SingleAsync(
            row => row.TenantId == tenantId && row.Name == "Demo brokerage");
        var transaction = await db.Transactions.SingleAsync(
            row => row.AccountId == account.Id && row.Reference == "TestSeed-DemoBrokerage-OpeningHoldings");
        var leg = await db.TransactionSecurityLegs.SingleAsync(row => row.TransactionId == transaction.Id);
        var instrument = await db.Instruments.SingleAsync(row => row.Id == leg.InstrumentId);
        var holding = await db.Holdings.SingleAsync(
            row => row.AccountId == account.Id && row.InstrumentId == instrument.Id);
        var cashLegCount = await db.TransactionCashLegs.CountAsync(row => row.TransactionId == transaction.Id);

        return new SeededOpening(
            transaction.PublicId,
            transaction.Type,
            transaction.CreatedBy,
            instrument.Symbol,
            leg.Quantity,
            leg.CostAmount,
            leg.CostCurrency,
            holding.PublicId,
            holding.Quantity,
            holding.CostAmount,
            holding.CostCurrency,
            cashLegCount);
    }

    private sealed record SeededOpening(
        Guid TransactionPublicId,
        TransactionType Type,
        string? CreatedBy,
        string Symbol,
        decimal Quantity,
        decimal CostAmount,
        string CostCurrency,
        Guid HoldingPublicId,
        decimal HoldingQuantity,
        decimal HoldingCostAmount,
        string HoldingCostCurrency,
        int CashLegCount);

    private static async Task<Tenant> EnsureDemoTenantAsync(ApplicationDbContext db)
    {
        var existing = await db.Tenants.SingleOrDefaultAsync(
            row => row.Code == DevelopmentIdentitySeeder.DemoTenantCode);
        if (existing is not null)
        {
            return existing;
        }

        var tenant = Tenant.Create(
            DevelopmentIdentitySeeder.DemoTenantName,
            DevelopmentIdentitySeeder.DemoTenantCode,
            Currency.Create("NZD", "New Zealand Dollar", 2));
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant;
    }

    private static async Task<User> EnsurePersonAsync(
        ApplicationDbContext db,
        Tenant tenant,
        UserRole role,
        string name,
        string email,
        int? adviserId = null)
    {
        var existing = await db.DomainUsers.SingleOrDefaultAsync(
            row => row.TenantId == tenant.Id && row.Email == email);
        if (existing is not null)
        {
            return existing;
        }

        var publicId = Guid.NewGuid();
        var identity = new ApplicationUser
        {
            UserName = publicId.ToString(),
            Email = email,
            EmailConfirmed = true,
            TenantId = tenant.Id
        };
        db.Users.Add(identity);
        await db.SaveChangesAsync();

        var person = User.Create(role, name, email, identity.Id, tenant.Id, adviserId, publicId: publicId);
        db.DomainUsers.Add(person);
        await db.SaveChangesAsync();
        return person;
    }

    private static async Task<List<Account>> LoadSeededAsync(ApplicationDbContext db)
    {
        var tenant = await db.Tenants.SingleAsync(row => row.Code == DevelopmentIdentitySeeder.DemoTenantCode);
        var customer = await db.DomainUsers.SingleAsync(
            row => row.TenantId == tenant.Id && row.Email == "customer@localhost");
        return await db.Accounts.AsNoTracking()
            .Where(row => row.CustomerId == customer.Id)
            .OrderBy(row => row.Name)
            .ToListAsync();
    }

    private static ApplicationDbContext OpenDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(SchemaDatabaseSetup.ConnectionString)
            .Options;
        return new ApplicationDbContext(options);
    }
}
