namespace MyWealthV2.Domain.Entities;

/// <summary>The rate a Transaction used. 0..1 per Transaction; immutable after create.</summary>
public class TransactionFxDetail
{
    private TransactionFxDetail()
    {
    }

    public int TransactionId { get; private set; }

    public string BaseCurrency { get; private set; } = string.Empty;

    public string QuoteCurrency { get; private set; } = string.Empty;

    public decimal Rate { get; private set; }

    public DateOnly RateAsOfDate { get; private set; }

    public string RateSource { get; private set; } = string.Empty;
}
