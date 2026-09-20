using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.ValueObjects;
using MyWealthV2.Infrastructure.Identity;
using MyWealthV2.Infrastructure.MarketData;

namespace MyWealthV2.Infrastructure.Data;

public static class TestSeed
{
    public const string CreatedBy = "system";

    public const string DemoCustomerEmail = "customer@localhost";

    public const string DemoCustomerName = "Demo Customer";

    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        var prices = services.GetRequiredService<MarketDataMock>();
        await SeedAsync(db, prices, cancellationToken);
        await EnsureDemoCustomerAsync(services, db, cancellationToken);
        await EnsureDemoAccountsAsync(db, cancellationToken);
    }

    public static async Task SeedAsync(
        ApplicationDbContext db,
        MarketDataMock prices,
        CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.SingleOrDefaultAsync(
            row => row.Code == DevelopmentIdentitySeeder.DemoTenantCode, cancellationToken);
        if (tenant is null)
        {
            return;
        }

        var usd = Currency.Create("USD", "United States Dollar", 2);
        var nzd = Currency.Create("NZD", "New Zealand Dollar", 2);

        var vti = await EnsureInstrumentAsync(
            db, tenant.Id, "VTI", "Vanguard Total Stock Market ETF", usd, enabled: true, cancellationToken);
        var aia = await EnsureInstrumentAsync(
            db, tenant.Id, "AIA", "Auckland International Airport", nzd, enabled: true, cancellationToken);
        await EnsureInstrumentAsync(
            db, tenant.Id, "NZD-CASH", "Demo disabled cash proxy", nzd, enabled: false, cancellationToken);

        prices.SetPrice(vti.Id, Money.Create(250m, "USD"));
        prices.SetPrice(aia.Id, Money.Create(8.50m, "NZD"));

        await EnsureDemoAccountsAsync(db, cancellationToken);
    }

    private static async Task<Instrument> EnsureInstrumentAsync(
        ApplicationDbContext db,
        int tenantId,
        string symbol,
        string name,
        Currency quoteCurrency,
        bool enabled,
        CancellationToken cancellationToken)
    {
        var existing = await db.Instruments.SingleOrDefaultAsync(
            row => row.TenantId == tenantId && row.Symbol == symbol, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var instrument = Instrument.Create(tenantId, symbol, name, quoteCurrency);
        if (!enabled)
        {
            instrument.Disable();
        }

        instrument.Created = TimeProvider.System.GetUtcNow();
        instrument.CreatedBy = CreatedBy;
        db.Instruments.Add(instrument);
        await db.SaveChangesAsync(cancellationToken);
        return instrument;
    }

    private static async Task EnsureDemoCustomerAsync(
        IServiceProvider services,
        ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.SingleOrDefaultAsync(
            row => row.Code == DevelopmentIdentitySeeder.DemoTenantCode, cancellationToken);
        if (tenant is null)
        {
            return;
        }

        var adviser = await db.DomainUsers.SingleOrDefaultAsync(
            row => row.TenantId == tenant.Id
                   && row.Role == UserRole.Adviser
                   && row.Email == DevelopmentIdentitySeeder.AdviserEmail,
            cancellationToken);
        if (adviser is null)
        {
            return;
        }

        if (await db.DomainUsers.AnyAsync(
                row => row.TenantId == tenant.Id && row.Email == DemoCustomerEmail, cancellationToken))
        {
            return;
        }

        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var publicId = Guid.NewGuid();
        var identityUser = new ApplicationUser
        {
            UserName = publicId.ToString(),
            Email = DemoCustomerEmail,
            EmailConfirmed = true,
            TenantId = tenant.Id
        };

        var result = await userManager.CreateAsync(
            identityUser, DevelopmentIdentitySeeder.AdviserPassword);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                string.Join("; ", result.Errors.Select(error => error.Description)));
        }

        var customer = User.Create(
            UserRole.Customer,
            DemoCustomerName,
            DemoCustomerEmail,
            identityUser.Id,
            tenant.Id,
            adviser.Id,
            publicId: publicId);
        customer.Created = TimeProvider.System.GetUtcNow();
        customer.CreatedBy = CreatedBy;
        db.DomainUsers.Add(customer);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task EnsureDemoAccountsAsync(
        ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.SingleOrDefaultAsync(
            row => row.Code == DevelopmentIdentitySeeder.DemoTenantCode, cancellationToken);
        if (tenant is null)
        {
            return;
        }

        var customer = await db.DomainUsers.SingleOrDefaultAsync(
            row => row.TenantId == tenant.Id && row.Email == DemoCustomerEmail, cancellationToken);
        if (customer is null)
        {
            return;
        }

        var usd = Currency.Create("USD", "United States Dollar", 2);
        var nzd = Currency.Create("NZD", "New Zealand Dollar", 2);

        await EnsureAccountAsync(
            db, tenant.Id, customer.Id, "Demo everyday bank", AccountType.Bank, nzd, closed: false,
            cancellationToken);
        await EnsureAccountAsync(
            db, tenant.Id, customer.Id, "Demo brokerage", AccountType.Brokerage, usd, closed: false,
            cancellationToken);
        await EnsureAccountAsync(
            db, tenant.Id, customer.Id, "Demo cash tin", AccountType.Cash, nzd, closed: false,
            cancellationToken);
        await EnsureAccountAsync(
            db, tenant.Id, customer.Id, "Demo closed other", AccountType.Other, nzd, closed: true,
            cancellationToken);
    }

    private static async Task EnsureAccountAsync(
        ApplicationDbContext db,
        int tenantId,
        int customerId,
        string name,
        AccountType type,
        Currency currency,
        bool closed,
        CancellationToken cancellationToken)
    {
        var existing = await db.Accounts.SingleOrDefaultAsync(
            row => row.CustomerId == customerId && row.Name == name, cancellationToken);
        if (existing is not null)
        {
            return;
        }

        var account = Account.Create(tenantId, customerId, name, type, currency);
        if (closed)
        {
            account.Close();
        }

        account.Created = TimeProvider.System.GetUtcNow();
        account.CreatedBy = CreatedBy;
        db.Accounts.Add(account);
        await db.SaveChangesAsync(cancellationToken);
    }
}
