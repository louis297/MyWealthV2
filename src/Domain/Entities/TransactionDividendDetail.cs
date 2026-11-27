namespace MyWealthV2.Domain.Entities;

/// <summary>
/// The dividend behind a <c>Dividend</c> Transaction (dividends R39): the Instrument, the entitled quantity, the amount
/// per share and its currency, the tax source, the imputation credit in the Account's currency, the three dates, and
/// with a reinvestment the reinvested quantity and the plan price. Exactly one on every <c>Dividend</c> and on its
/// Reversal, none elsewhere; immutable. The amount per share and the ex-date are always set by a corporate action's
/// dividend and may be missing on a hand-entered one (external-income R17).
/// </summary>
public class TransactionDividendDetail
{
    private TransactionDividendDetail()
    {
    }

    public int TransactionId { get; private set; }

    public int TenantId { get; private set; }

    public int InstrumentId { get; private set; }

    public decimal Quantity { get; private set; }

    public decimal? AmountPerShare { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    public DividendTaxSource TaxSource { get; private set; }

    public decimal ImputationCredit { get; private set; }

    public decimal? ReinvestedQuantity { get; private set; }

    public decimal? ReinvestmentPrice { get; private set; }

    public DateOnly? ExDate { get; private set; }

    public DateOnly? RecordDate { get; private set; }

    public DateOnly? PaymentDate { get; private set; }

    internal static TransactionDividendDetail Create(
        int tenantId,
        int instrumentId,
        decimal quantity,
        decimal? amountPerShare,
        string currency,
        DividendTaxSource taxSource,
        decimal imputationCredit,
        decimal? reinvestedQuantity,
        decimal? reinvestmentPrice,
        DateOnly? exDate,
        DateOnly? recordDate,
        DateOnly? paymentDate)
    {
        if (quantity <= 0m || amountPerShare <= 0m || imputationCredit < 0m)
        {
            throw new DomainException("A dividend detail has a quantity above 0, no amount per share or one above 0, and a credit of 0 or more.");
        }

        if ((reinvestedQuantity is null) != (reinvestmentPrice is null))
        {
            throw new DomainException("A dividend detail has the reinvested quantity and the plan price together.");
        }

        return new TransactionDividendDetail
        {
            TenantId = tenantId,
            InstrumentId = instrumentId,
            Quantity = quantity,
            AmountPerShare = amountPerShare,
            Currency = currency,
            TaxSource = taxSource,
            ImputationCredit = imputationCredit,
            ReinvestedQuantity = reinvestedQuantity,
            ReinvestmentPrice = reinvestmentPrice,
            ExDate = exDate,
            RecordDate = recordDate,
            PaymentDate = paymentDate
        };
    }

    /// <summary>A new row with the same values, for the Reversal (R39, R42).</summary>
    internal TransactionDividendDetail Copy() =>
        Create(TenantId, InstrumentId, Quantity, AmountPerShare, Currency, TaxSource, ImputationCredit, ReinvestedQuantity,
            ReinvestmentPrice, ExDate, RecordDate, PaymentDate);
}
