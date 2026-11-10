namespace MyWealthV2.Domain.ValueObjects;

/// <summary>The rate a Transaction was priced at: quote currency per 1 base currency.</summary>
public class AppliedFxRate : ValueObject
{
    private AppliedFxRate()
    {
    }

    public string BaseCurrency { get; private set; } = string.Empty;

    public string QuoteCurrency { get; private set; } = string.Empty;

    public decimal Rate { get; private set; }

    public DateOnly AsOfDate { get; private set; }

    public string Source { get; private set; } = string.Empty;

    public static AppliedFxRate Create(
        string baseCurrency,
        string quoteCurrency,
        decimal rate,
        DateOnly asOfDate,
        string source)
    {
        var baseCode = Money.Create(0m, baseCurrency).Currency;
        var quoteCode = Money.Create(0m, quoteCurrency).Currency;
        if (string.Equals(baseCode, quoteCode, StringComparison.Ordinal))
        {
            throw new DomainException("The base and quote currencies must differ.");
        }

        if (rate <= 0)
        {
            throw new DomainException("Rate must be greater than 0.");
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            throw new DomainException("Rate source is required.");
        }

        return new AppliedFxRate
        {
            BaseCurrency = baseCode,
            QuoteCurrency = quoteCode,
            Rate = rate,
            AsOfDate = asOfDate,
            Source = source.Trim()
        };
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return BaseCurrency;
        yield return QuoteCurrency;
        yield return Rate;
        yield return AsOfDate;
        yield return Source;
    }
}
