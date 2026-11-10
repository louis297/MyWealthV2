using MyWealthV2.Domain.ValueObjects;

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

    internal static TransactionFxDetail Create(AppliedFxRate rate) =>
        new()
        {
            BaseCurrency = rate.BaseCurrency,
            QuoteCurrency = rate.QuoteCurrency,
            Rate = rate.Rate,
            RateAsOfDate = rate.AsOfDate,
            RateSource = rate.Source
        };

    /// <summary>A new row with the same rate, for the Reversal (FX conversions R17).</summary>
    internal TransactionFxDetail Copy() =>
        new()
        {
            BaseCurrency = BaseCurrency,
            QuoteCurrency = QuoteCurrency,
            Rate = Rate,
            RateAsOfDate = RateAsOfDate,
            RateSource = RateSource
        };
}
