namespace MyWealthV2.Domain.CorporateActions;

/// <summary>Dividends R30: the shares the custodian delivered, shared out to the reinvesting entitlements.</summary>
public static class DividendAllocation
{
    public static IReadOnlyList<decimal> Allocate(decimal shares, IReadOnlyList<(int AccountId, decimal Participating)> entitlements) =>
        throw new NotImplementedException();
}
