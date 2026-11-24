namespace MyWealthV2.Domain.CorporateActions;

/// <summary>
/// Dividends R30: the shares the custodian delivered, shared out to the reinvesting entitlements in proportion to their
/// participating quantities. Each share is rounded down to 8 decimal places, and the units of <c>0.00000001</c> left
/// over go one each to the entitlements with the largest remainders, then by account <c>Id</c>. The shares add up to
/// what was delivered exactly.
/// </summary>
public static class DividendAllocation
{
    private const decimal Unit = 0.00000001m;

    /// <summary>The reinvested quantity of each entitlement, in the order given.</summary>
    public static IReadOnlyList<decimal> Allocate(decimal shares, IReadOnlyList<(int AccountId, decimal Participating)> entitlements)
    {
        var total = entitlements.Sum(entry => entry.Participating);
        if (entitlements.Count == 0 || total <= 0m || shares <= 0m)
        {
            throw new DomainException("Shares are shared out over reinvesting entitlements with a participating quantity.");
        }

        var exact = entitlements.Select(entry => shares * entry.Participating / total).ToList();
        var allocated = exact
            .Select(value => Math.Round(value, CorporateActionRatio.QuantityDecimals, MidpointRounding.ToZero))
            .ToList();
        var left = (int)Math.Round((shares - allocated.Sum()) / Unit);
        var order = Enumerable.Range(0, entitlements.Count)
            .OrderByDescending(index => exact[index] - allocated[index])
            .ThenBy(index => entitlements[index].AccountId)
            .ToList();
        for (var unit = 0; unit < left; unit++)
        {
            allocated[order[unit % order.Count]] += Unit;
        }

        return allocated;
    }
}
