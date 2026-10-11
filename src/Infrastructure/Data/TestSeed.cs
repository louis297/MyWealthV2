using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Domain.Common;
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
        await EnsureDemoAccountsAsync(db, prices, cancellationToken);
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

        await EnsureDemoAccountsAsync(db, prices, cancellationToken);
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
        MarketDataMock prices,
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
        await EnsureDemoEverydayTransferAsync(db, tenant.Id, customer.Id, cancellationToken);
        await EnsureDemoBrokerageOpeningAsync(db, tenant.Id, customer.Id, cancellationToken);
        await EnsureDemoBrokerageFundingAsync(db, tenant.Id, customer.Id, cancellationToken);
        await EnsureDemoBrokerageBuyAsync(db, prices, tenant.Id, customer.Id, cancellationToken);
        await EnsureDemoBrokerageStockInAsync(db, tenant.Id, customer.Id, cancellationToken);
        await EnsureDemoBrokerageSplitAsync(db, tenant.Id, customer.Id, cancellationToken);
        await SyncDemoBrokerageHoldingAsync(db, tenant.Id, customer.Id, cancellationToken);
    }

    private const string DemoBrokerageOpeningReference = "TestSeed-DemoBrokerage-OpeningHoldings";

    private static async Task EnsureDemoBrokerageOpeningAsync(
        ApplicationDbContext db,
        int tenantId,
        int customerId,
        CancellationToken cancellationToken)
    {
        var account = await db.Accounts.SingleOrDefaultAsync(
            row => row.CustomerId == customerId && row.Name == "Demo brokerage", cancellationToken);
        if (account is null)
        {
            return;
        }

        var exists = await db.Transactions.AnyAsync(
            row => row.AccountId == account.Id && row.Reference == DemoBrokerageOpeningReference,
            cancellationToken);
        if (exists)
        {
            return;
        }

        var vti = await db.Instruments.SingleOrDefaultAsync(
            row => row.TenantId == tenantId && row.Symbol == "VTI", cancellationToken);
        if (vti is null)
        {
            return;
        }

        var bookedAt = new DateTimeOffset(2026, 1, 15, 9, 30, 0, TimeSpan.FromHours(13));
        var transaction = Transaction.PostOpeningHoldings(
            tenantId,
            account.Id,
            account.Type,
            bookedAt,
            "Demo opening holdings",
            DemoBrokerageOpeningReference,
            [OpeningSecurityLeg.Create(vti.Id, 10m, 2500.00m, vti.QuoteCurrency)]);
        var holding = Holding.Create(tenantId, account.Id, vti.Id, 10m, 2500.00m, vti.QuoteCurrency);
        var created = TimeProvider.System.GetUtcNow();
        transaction.Created = created;
        transaction.CreatedBy = CreatedBy;
        holding.Created = created;
        holding.CreatedBy = CreatedBy;
        foreach (var leg in transaction.SecurityLegs)
        {
            leg.Created = created;
            leg.CreatedBy = CreatedBy;
        }

        db.Transactions.Add(transaction);
        db.Holdings.Add(holding);
        await db.SaveChangesAsync(cancellationToken);
    }

    private const string DemoBrokerageFundingReference = "TestSeed-DemoBrokerage-Funding";
    private const string DemoBrokerageBuyReference = "TestSeed-DemoBrokerage-Buy";
    private const string DemoBrokerageStockInReference = "TestSeed-DemoBrokerage-StockIn";
    private const string DemoBrokerageSplitReference = "TestSeed-DemoBrokerage-Split";

    private static async Task EnsureDemoBrokerageFundingAsync(
        ApplicationDbContext db,
        int tenantId,
        int customerId,
        CancellationToken cancellationToken)
    {
        var account = await DemoBrokerageAsync(db, customerId, cancellationToken);
        if (account is null || await ReferenceExistsAsync(db, account.Id, DemoBrokerageFundingReference, cancellationToken))
        {
            return;
        }

        var bookedAt = new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.FromHours(13));
        var transaction = Transaction.Post(
            tenantId,
            account.Id,
            TransactionType.TransferIn,
            1000m,
            account.Currency,
            bookedAt,
            "Demo brokerage funding",
            DemoBrokerageFundingReference,
            currentCashSum: await CashSumAsync(db, account.Id, cancellationToken),
            existingTransactionCount: 1);
        Stamp(transaction, transaction.CashLeg!);
        db.Transactions.Add(transaction);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task EnsureDemoBrokerageBuyAsync(
        ApplicationDbContext db,
        MarketDataMock prices,
        int tenantId,
        int customerId,
        CancellationToken cancellationToken)
    {
        var account = await DemoBrokerageAsync(db, customerId, cancellationToken);
        if (account is null || await ReferenceExistsAsync(db, account.Id, DemoBrokerageBuyReference, cancellationToken))
        {
            return;
        }

        var vti = await db.Instruments.SingleOrDefaultAsync(
            row => row.TenantId == tenantId && row.Symbol == "VTI", cancellationToken);
        var price = vti is null ? null : prices.TryGetPrice(vti.Id);
        if (vti is null || price is null)
        {
            return;
        }

        var quote = vti.QuoteCurrency.Trim();
        if (!string.Equals(quote, account.Currency.Trim(), StringComparison.OrdinalIgnoreCase)
            || !string.Equals(price.Currency, quote, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var bookedAt = new DateTimeOffset(2026, 1, 15, 10, 30, 0, TimeSpan.FromHours(13));
        var transaction = Transaction.PostBuy(
            tenantId,
            account.Id,
            account.Type,
            account.Currency,
            vti.Id,
            quantity: 2m,
            price,
            fxRate: 1m,
            bookedAt,
            memo: "Demo brokerage buy",
            reference: DemoBrokerageBuyReference,
            currentCashSum: await CashSumAsync(db, account.Id, cancellationToken));
        Stamp(transaction, transaction.CashLeg!);
        foreach (var leg in transaction.SecurityLegs)
        {
            Stamp(leg);
        }

        db.Transactions.Add(transaction);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task EnsureDemoBrokerageStockInAsync(
        ApplicationDbContext db,
        int tenantId,
        int customerId,
        CancellationToken cancellationToken)
    {
        var account = await DemoBrokerageAsync(db, customerId, cancellationToken);
        if (account is null || await ReferenceExistsAsync(db, account.Id, DemoBrokerageStockInReference, cancellationToken))
        {
            return;
        }

        var vti = await db.Instruments.SingleOrDefaultAsync(
            row => row.TenantId == tenantId && row.Symbol == "VTI", cancellationToken);
        if (vti is null)
        {
            return;
        }

        var bookedAt = new DateTimeOffset(2026, 1, 15, 11, 0, 0, TimeSpan.FromHours(13));
        var transaction = Transaction.PostStockIn(
            tenantId,
            account.Id,
            account.Type,
            vti.Id,
            quantity: 4m,
            cost: 800m,
            quoteCurrency: vti.QuoteCurrency,
            bookedAt,
            memo: "Demo brokerage stock in",
            reference: DemoBrokerageStockInReference);
        Stamp(transaction);
        foreach (var leg in transaction.SecurityLegs)
        {
            Stamp(leg);
        }

        db.Transactions.Add(transaction);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task EnsureDemoBrokerageSplitAsync(
        ApplicationDbContext db,
        int tenantId,
        int customerId,
        CancellationToken cancellationToken)
    {
        var account = await DemoBrokerageAsync(db, customerId, cancellationToken);
        if (account is null || await ReferenceExistsAsync(db, account.Id, DemoBrokerageSplitReference, cancellationToken))
        {
            return;
        }

        var vti = await db.Instruments.SingleOrDefaultAsync(
            row => row.TenantId == tenantId && row.Symbol == "VTI", cancellationToken);
        if (vti is null)
        {
            return;
        }

        var legs = await (
            from leg in db.TransactionSecurityLegs
            join transaction in db.Transactions on leg.TransactionId equals transaction.Id
            where transaction.AccountId == account.Id && leg.InstrumentId == vti.Id
            select new { leg.Quantity, leg.CostAmount }).ToListAsync(cancellationToken);
        if (legs.Count == 0)
        {
            return;
        }

        var quantity = legs.Sum(leg => leg.Quantity);
        var cost = legs.Sum(leg => leg.CostAmount);
        var bookedAt = new DateTimeOffset(2026, 1, 15, 11, 30, 0, TimeSpan.FromHours(13));
        var split = Transaction.PostSplit(
            tenantId,
            account.Id,
            account.Type,
            TransactionType.Split,
            vti.Id,
            newQuantity: quantity * 2m,
            quoteCurrency: vti.QuoteCurrency,
            bookedAt,
            memo: "Demo brokerage split",
            reference: DemoBrokerageSplitReference,
            currentQuantity: quantity,
            currentCost: cost);
        Stamp(split);
        foreach (var leg in split.SecurityLegs)
        {
            Stamp(leg);
        }

        db.Transactions.Add(split);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task SyncDemoBrokerageHoldingAsync(
        ApplicationDbContext db,
        int tenantId,
        int customerId,
        CancellationToken cancellationToken)
    {
        var account = await DemoBrokerageAsync(db, customerId, cancellationToken);
        if (account is null)
        {
            return;
        }

        var vti = await db.Instruments.SingleOrDefaultAsync(
            row => row.TenantId == tenantId && row.Symbol == "VTI", cancellationToken);
        if (vti is null)
        {
            return;
        }

        var legs = await (
            from leg in db.TransactionSecurityLegs
            join transaction in db.Transactions on leg.TransactionId equals transaction.Id
            where transaction.AccountId == account.Id && leg.InstrumentId == vti.Id
            select new { leg.Quantity, leg.CostAmount, leg.CostCurrency }).ToListAsync(cancellationToken);
        if (legs.Count == 0)
        {
            return;
        }

        var quantity = legs.Sum(leg => leg.Quantity);
        var cost = legs.Sum(leg => leg.CostAmount);
        var currency = legs[0].CostCurrency;
        var holding = await db.Holdings.SingleOrDefaultAsync(
            row => row.AccountId == account.Id && row.InstrumentId == vti.Id, cancellationToken);
        if (holding is null)
        {
            holding = Holding.Create(tenantId, account.Id, vti.Id, quantity, cost, currency);
            Stamp(holding);
            db.Holdings.Add(holding);
        }
        else if (holding.ReplaceTotals(quantity, cost, currency))
        {
            db.Holdings.Remove(holding);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static Task<Account?> DemoBrokerageAsync(
        ApplicationDbContext db,
        int customerId,
        CancellationToken cancellationToken) =>
        db.Accounts.SingleOrDefaultAsync(
            row => row.CustomerId == customerId && row.Name == "Demo brokerage", cancellationToken);

    private static Task<bool> ReferenceExistsAsync(
        ApplicationDbContext db,
        int accountId,
        string reference,
        CancellationToken cancellationToken) =>
        db.Transactions.AnyAsync(
            row => row.AccountId == accountId && row.Reference == reference, cancellationToken);

    private static Task<decimal> CashSumAsync(
        ApplicationDbContext db,
        int accountId,
        CancellationToken cancellationToken) =>
        (from leg in db.TransactionCashLegs
         join transaction in db.Transactions on leg.TransactionId equals transaction.Id
         where transaction.AccountId == accountId
         select leg.Amount).SumAsync(cancellationToken);

    private static void Stamp(params BaseAuditableEntity[] entities)
    {
        var created = TimeProvider.System.GetUtcNow();
        foreach (var entity in entities)
        {
            entity.Created = created;
            entity.CreatedBy = CreatedBy;
        }
    }

    private const string DemoEverydayBankTransferReference = "TestSeed-DemoEverydayBank-OpeningTransfer";

    private static async Task EnsureDemoEverydayTransferAsync(
        ApplicationDbContext db,
        int tenantId,
        int customerId,
        CancellationToken cancellationToken)
    {
        var account = await db.Accounts.SingleOrDefaultAsync(
            row => row.CustomerId == customerId && row.Name == "Demo everyday bank", cancellationToken);
        if (account is null)
        {
            return;
        }

        var exists = await db.Transactions.AnyAsync(
            row => row.AccountId == account.Id && row.Reference == DemoEverydayBankTransferReference,
            cancellationToken);
        if (exists)
        {
            return;
        }

        var bookedAt = new DateTimeOffset(2026, 1, 15, 9, 0, 0, TimeSpan.FromHours(13));
        var transaction = Transaction.Post(
            tenantId,
            account.Id,
            TransactionType.TransferIn,
            100m,
            account.Currency,
            bookedAt,
            "Demo opening transfer",
            DemoEverydayBankTransferReference,
            currentCashSum: 0m,
            existingTransactionCount: 0);
        var created = TimeProvider.System.GetUtcNow();
        transaction.Created = created;
        transaction.CreatedBy = CreatedBy;
        transaction.CashLeg!.Created = created;
        transaction.CashLeg.CreatedBy = CreatedBy;
        db.Transactions.Add(transaction);
        await db.SaveChangesAsync(cancellationToken);
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
