namespace MyWealthV2.Domain.CorporateActions;

/// <summary>Dividends R15: what the custodian should pay into the client money account for a dividend.</summary>
public static class DividendPayment
{
    /// <summary>The sum over the entitlements of <c>grossAmount − foreignTax</c>, less what the custodian reinvests
    /// under an accepted election. Entitlements of another kind count nothing.</summary>
    public static decimal ExpectedAmount(IEnumerable<CorporateActionEntitlement> entitlements, decimal reinvested) =>
        entitlements.Sum(row => (row.GrossAmount ?? 0m) - (row.ForeignTax ?? 0m)) - reinvested;
}
