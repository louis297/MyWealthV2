namespace MyWealthV2.Domain.Valuations;

/// <summary>
/// The valuation of one Custodial account for one UTC day as the valuation run stored it (valuation-run R8): the
/// figures of an <see cref="AccountValuation"/>, nothing recomputed, with its lines, the run that stored it and when.
/// Never changed after it is stored (R10).
/// </summary>
public class StoredValuation
{
    private readonly List<StoredValuationLine> _lines = [];

    private StoredValuation()
    {
    }

    public long Id { get; private set; }

    public int TenantId { get; private set; }

    public int AccountId { get; private set; }

    public DateOnly Date { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    public decimal Cash { get; private set; }

    public decimal HoldingsValue { get; private set; }

    public decimal Total { get; private set; }

    public bool Complete { get; private set; }

    public int UnvaluedCount { get; private set; }

    public int ValuationRunId { get; private set; }

    public DateTimeOffset ValuedAt { get; private set; }

    public IReadOnlyList<StoredValuationLine> Lines => _lines;
}

/// <summary>One line of a stored valuation: a <see cref="ValuationLine"/> with the price row and the rate row it
/// read (valuation-run R8). Symbol and name are not stored.</summary>
public class StoredValuationLine
{
    private StoredValuationLine()
    {
    }

    public long Id { get; private set; }

    public int TenantId { get; private set; }

    public long StoredValuationId { get; private set; }

    public int InstrumentId { get; private set; }

    public decimal Quantity { get; private set; }

    public decimal LedgerQuantity { get; private set; }

    public ValuationLineStatus Status { get; private set; }

    public decimal? PriceAmount { get; private set; }

    public string? PriceCurrency { get; private set; }

    public DateOnly? PriceDate { get; private set; }

    public string? PriceSource { get; private set; }

    public decimal? MarketValue { get; private set; }

    public decimal? FxRate { get; private set; }

    public DateOnly? FxDate { get; private set; }

    public string? FxSource { get; private set; }

    public decimal? Value { get; private set; }
}
