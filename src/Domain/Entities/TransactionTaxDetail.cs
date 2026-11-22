namespace MyWealthV2.Domain.Entities;

/// <summary>
/// How one <see cref="CashLegRole.Tax"/> cash leg was computed (interest-run R25): tax kind, rate, basis (the gross),
/// tax amount and currency. Exactly one per <c>Tax</c> leg; immutable after create.
/// </summary>
public class TransactionTaxDetail
{
    private TransactionTaxDetail()
    {
    }

    public int CashLegId { get; private set; }

    public int TenantId { get; private set; }

    public int TransactionId { get; private set; }

    public TaxKind Kind { get; private set; }

    public decimal RatePercent { get; private set; }

    public decimal BasisAmount { get; private set; }

    public decimal TaxAmount { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    internal static TransactionTaxDetail Create(
        int tenantId,
        TaxKind kind,
        decimal ratePercent,
        decimal basisAmount,
        decimal taxAmount,
        string currency) =>
        new()
        {
            TenantId = tenantId,
            Kind = kind,
            RatePercent = ratePercent,
            BasisAmount = basisAmount,
            TaxAmount = taxAmount,
            Currency = currency
        };

    /// <summary>A new row with the same values, for the matching leg of a Reversal (interest-run R27).</summary>
    internal TransactionTaxDetail Copy() => Create(TenantId, Kind, RatePercent, BasisAmount, TaxAmount, Currency);
}
