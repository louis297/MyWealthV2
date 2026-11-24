namespace MyWealthV2.Domain.Entities;

/// <summary>
/// The dividend behind a <c>Dividend</c> Transaction (dividends R39): the Instrument, the entitled quantity, the amount
/// per share and its currency, the tax source, the imputation credit in the Account's currency, the three dates, and
/// with a reinvestment the reinvested quantity and the plan price. Exactly one on every <c>Dividend</c> and on its
/// Reversal, none elsewhere; immutable.
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

    public decimal AmountPerShare { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    public DividendTaxSource TaxSource { get; private set; }

    public decimal ImputationCredit { get; private set; }

    public decimal? ReinvestedQuantity { get; private set; }

    public decimal? ReinvestmentPrice { get; private set; }

    public DateOnly ExDate { get; private set; }

    public DateOnly? RecordDate { get; private set; }

    public DateOnly? PaymentDate { get; private set; }
}
