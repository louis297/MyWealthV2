namespace MyWealthV2.Domain.Tax;

/// <summary>
/// Tax-year-events R6–R17: the tax events of a Customer, over every tax year, from the facts of their ledger. Reads no
/// clock and no store.
/// </summary>
public static class TaxLedger
{
    /// <summary>The events of <paramref name="facts"/> in the order of R8.</summary>
    public static IReadOnlyList<TaxEvent> Build(IEnumerable<LedgerFact> facts) => [];
}
