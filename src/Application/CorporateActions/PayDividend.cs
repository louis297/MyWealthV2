using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.CorporateActions;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.CorporateActions;

/// <summary>
/// Dividends R29, R30: a dividend becomes paid in the database transaction that completes the last of its conditions.
/// The bank job, the hand match, the custodian job and pay in cash each call this after their own change. With
/// shares, the same transaction shares them out exactly to the reinvesting entitlements. The apply step then posts the
/// entitlements (R36).
/// </summary>
internal static class PayDividend
{
    public static async Task<bool> TryAsync(
        IApplicationDbContext db,
        CorporateAction action,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        if (!action.TryPay(at))
        {
            return false;
        }

        if (action.SharesReceived is { } shares)
        {
            var reinvesting = await db.CorporateActionEntitlements.IgnoreQueryFilters()
                .Where(row => row.CorporateActionId == action.Id && row.Reinvests && row.Status == EntitlementStatus.Pending)
                .OrderBy(row => row.AccountId)
                .ToListAsync(cancellationToken);
            var quantities = DividendAllocation.Allocate(
                shares, reinvesting.Select(row => (row.AccountId, row.ParticipatingQuantity!.Value)).ToList());
            for (var index = 0; index < reinvesting.Count; index++)
            {
                if (quantities[index] > 0m)
                {
                    reinvesting[index].SetReinvestedQuantity(quantities[index]);
                }
            }
        }

        return true;
    }
}
