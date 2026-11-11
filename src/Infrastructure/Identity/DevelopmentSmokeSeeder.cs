using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Infrastructure.Identity;

/// <summary>
/// Development-only seed for the smoke tenant used by tests/Smoke.Tests (docs/smoke-test.md §3).
/// </summary>
public static class DevelopmentSmokeSeeder
{
    public const string TenantCode = "smoke";
    public const string TenantName = "Smoke Test";
    public const string AdviserEmail = "smoke-adviser@localhost";
    public const string AdviserName = "Smoke Adviser";
    public const string CustomerEmail = "smoke-customer@localhost";
    public const string CustomerName = "Smoke Customer";
    public const string AccountName = "Smoke Cash";

    public const string AdviserPasswordKey = "Smoke:AdviserPassword";
    public const string CustomerPasswordKey = "Smoke:CustomerPassword";

    private const string CreatedBy = "system";
    private const string TransferInReference = "Smoke-Cash-TransferIn";

    public static async Task SeedAsync(
        IServiceProvider services,
        string? adviserPassword,
        string? customerPassword,
        CancellationToken cancellationToken)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DevelopmentSmokeSeeder));
        if (string.IsNullOrWhiteSpace(adviserPassword) || string.IsNullOrWhiteSpace(customerPassword))
        {
            var missing = string.IsNullOrWhiteSpace(adviserPassword) ? AdviserPasswordKey : CustomerPasswordKey;
            logger.LogWarning("Smoke tenant not seeded: configuration key {Key} is not set.", missing);
            return;
        }

        var db = services.GetRequiredService<ApplicationDbContext>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var catalog = services.GetRequiredService<ICurrencyCatalog>();
        var nzd = catalog.TryGet("NZD")
                  ?? throw new InvalidOperationException("NZD must exist before seeding the smoke tenant.");

        var tenant = await db.Tenants.SingleOrDefaultAsync(row => row.Code == TenantCode, cancellationToken);
        if (tenant is null)
        {
            tenant = Tenant.Create(TenantName, TenantCode, nzd);
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync(cancellationToken);
        }

        await DevelopmentIdentitySeeder.EnsurePersonAsync(
            db, userManager, UserRole.Adviser, AdviserName, AdviserEmail, adviserPassword,
            tenant.Id, cancellationToken);
        var adviser = await db.DomainUsers.SingleAsync(
            row => row.TenantId == tenant.Id && row.Role == UserRole.Adviser && row.Email == AdviserEmail,
            cancellationToken);

        await DevelopmentIdentitySeeder.EnsurePersonAsync(
            db, userManager, UserRole.Customer, CustomerName, CustomerEmail, customerPassword,
            tenant.Id, cancellationToken, adviserId: adviser.Id);
        var customer = await db.DomainUsers.SingleAsync(
            row => row.TenantId == tenant.Id && row.Role == UserRole.Customer && row.Email == CustomerEmail,
            cancellationToken);

        var account = await db.Accounts.SingleOrDefaultAsync(
            row => row.CustomerId == customer.Id && row.Name == AccountName, cancellationToken);
        if (account is null)
        {
            account = Account.Create(tenant.Id, customer.Id, AccountName, AccountType.Bank, nzd);
            account.Created = TimeProvider.System.GetUtcNow();
            account.CreatedBy = CreatedBy;
            db.Accounts.Add(account);
            await db.SaveChangesAsync(cancellationToken);
        }

        var transferred = await db.Transactions.AnyAsync(
            row => row.TenantId == tenant.Id && row.Reference == TransferInReference, cancellationToken);
        if (transferred)
        {
            return;
        }

        var transaction = Transaction.Post(
            tenant.Id,
            account.Id,
            TransactionType.TransferIn,
            1000m,
            account.Currency,
            new DateTimeOffset(2026, 1, 15, 9, 0, 0, TimeSpan.FromHours(13)),
            "Smoke opening transfer",
            TransferInReference,
            currentCashSum: 0m,
            existingTransactionCount: 0);
        var created = TimeProvider.System.GetUtcNow();
        transaction.Created = created;
        transaction.CreatedBy = CreatedBy;
        var leg = transaction.CashLegs.Single();
        leg.Created = created;
        leg.CreatedBy = CreatedBy;
        db.Transactions.Add(transaction);
        await db.SaveChangesAsync(cancellationToken);
    }
}
