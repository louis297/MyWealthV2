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

    /// <summary>R8: copies <paramref name="valuation"/> of the Account <paramref name="accountId"/>, stored by the run
    /// <paramref name="runId"/> at <paramref name="at"/>. The only way to build one.</summary>
    public static StoredValuation From(int tenantId, int accountId, AccountValuation valuation, int runId, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(valuation);
        var values = valuation.Lines.Where(line => line.Status == ValuationLineStatus.Valued).Sum(line => line.Value ?? 0m);
        if (valuation.HoldingsValue != values || valuation.Total != valuation.Cash + valuation.HoldingsValue)
        {
            throw new DomainException("A stored valuation's total is its cash plus the value of every Valued line.");
        }

        var unvalued = valuation.Lines.Count(line => line.Status != ValuationLineStatus.Valued);
        if (valuation.UnvaluedCount != unvalued || valuation.Complete != (unvalued == 0))
        {
            throw new DomainException("A stored valuation is complete only when every line is Valued.");
        }

        var stored = new StoredValuation
        {
            TenantId = tenantId,
            AccountId = accountId,
            Date = valuation.Day,
            Currency = valuation.Currency,
            Cash = valuation.Cash,
            HoldingsValue = valuation.HoldingsValue,
            Total = valuation.Total,
            Complete = valuation.Complete,
            UnvaluedCount = valuation.UnvaluedCount,
            ValuationRunId = runId,
            ValuedAt = at
        };
        stored._lines.AddRange(valuation.Lines.Select(line => StoredValuationLine.From(tenantId, line)));
        return stored;
    }
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

    internal static StoredValuationLine From(int tenantId, ValuationLine line)
    {
        if ((line.Status == ValuationLineStatus.Valued) != (line.Value is not null))
        {
            throw new DomainException("A line has a value if and only if it is Valued.");
        }

        return new StoredValuationLine
        {
            TenantId = tenantId,
            InstrumentId = line.InstrumentId,
            Quantity = line.Quantity,
            LedgerQuantity = line.LedgerQuantity,
            Status = line.Status,
            PriceAmount = line.Price?.Amount,
            PriceCurrency = line.Price?.Currency,
            PriceDate = line.Price?.AsOfDate,
            PriceSource = line.Price?.Source,
            MarketValue = line.MarketValue,
            FxRate = line.Fx?.Rate,
            FxDate = line.Fx?.AsOfDate,
            FxSource = line.Fx?.Source,
            Value = line.Value
        };
    }
}
