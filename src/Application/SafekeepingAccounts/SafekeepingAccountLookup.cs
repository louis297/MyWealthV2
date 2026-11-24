using FluentValidation.Results;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Entities;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.SafekeepingAccounts;

internal static class SafekeepingAccountLookup
{
    public const string AccountNumberInUseMessage =
        "This account number is already in use by an active safekeeping account.";

    public static string CurrencyTakenMessage(string currency) =>
        $"The tenant already has an active safekeeping account in {currency}.";

    /// <summary>The tenant by PublicId, else 404 (R4).</summary>
    public static async Task<Tenant> TenantAsync(IApplicationDbContext db, Guid tenantPublicId, CancellationToken cancellationToken) =>
        await db.Tenants.AsNoTracking().SingleOrDefaultAsync(row => row.PublicId == tenantPublicId, cancellationToken)
        ?? throw new NotFoundException("Tenant", tenantPublicId);

    /// <summary>The tracked row <paramref name="id"/> of the tenant; unknown tenant or another tenant's row → 404 (R4).</summary>
    public static async Task<SafekeepingAccount> FindAsync(
        IApplicationDbContext db, Guid tenantPublicId, Guid id, CancellationToken cancellationToken)
    {
        var tenant = await TenantAsync(db, tenantPublicId, cancellationToken);
        return await db.SafekeepingAccounts.IgnoreQueryFilters()
                   .SingleOrDefaultAsync(row => row.TenantId == tenant.Id && row.PublicId == id, cancellationToken)
               ?? throw new NotFoundException("Safekeeping account", id);
    }

    /// <summary>R5: one active per tenant and currency; an active account number is unique on the platform.</summary>
    public static async Task EnsureCanBeActiveAsync(
        IApplicationDbContext db, int tenantId, string currency, string accountNumber, int? exceptId, CancellationToken cancellationToken)
    {
        var active = db.SafekeepingAccounts.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.IsActive && row.Id != (exceptId ?? 0));

        if (await active.AnyAsync(row => row.TenantId == tenantId && row.Currency == currency, cancellationToken))
        {
            throw new ValidationException([new ValidationFailure(nameof(SafekeepingAccount.Currency), CurrencyTakenMessage(currency))]);
        }

        // The database collation compares the account number without regard to case.
        if (await active.AnyAsync(row => row.AccountNumber == accountNumber, cancellationToken))
        {
            throw new ValidationException([new ValidationFailure(nameof(SafekeepingAccount.AccountNumber), AccountNumberInUseMessage)]);
        }
    }
}
