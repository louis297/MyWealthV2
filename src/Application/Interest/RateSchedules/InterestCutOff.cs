using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.Interest.RateSchedules;

/// <summary>
/// Interest-run R12: the cut-off date for a tenant and currency is the later of today (UTC) and the latest
/// <c>InterestAccruedThrough</c> of the tenant's Custodial accounts in that currency.
/// </summary>
public static class InterestCutOff
{
    public static DateOnly Today(TimeProvider time) => DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);

    public static async Task<DateOnly> ForAsync(
        IApplicationDbContext db,
        int tenantId,
        string currency,
        DateOnly today,
        CancellationToken cancellationToken) =>
        (await ForAsync(db, tenantId, [currency], today, cancellationToken))[currency];

    public static async Task<Dictionary<string, DateOnly>> ForAsync(
        IApplicationDbContext db,
        int tenantId,
        IReadOnlyCollection<string> currencies,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var codes = currencies.Distinct(StringComparer.Ordinal).ToList();
        var latest = await db.Accounts.IgnoreQueryFilters()
            .Where(account => account.TenantId == tenantId
                              && account.CustodyMode == CustodyMode.Custodial
                              && account.InterestAccruedThrough != null
                              && codes.Contains(account.Currency))
            .GroupBy(account => account.Currency)
            .Select(group => new { Currency = group.Key, Latest = group.Max(account => account.InterestAccruedThrough) })
            .ToListAsync(cancellationToken);

        var result = codes.ToDictionary(code => code, _ => today, StringComparer.Ordinal);
        foreach (var row in latest)
        {
            var code = row.Currency.Trim().ToUpperInvariant();
            if (row.Latest is { } day && result.TryGetValue(code, out var current) && day > current)
            {
                result[code] = day;
            }
        }

        return result;
    }
}
