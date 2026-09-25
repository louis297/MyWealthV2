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
