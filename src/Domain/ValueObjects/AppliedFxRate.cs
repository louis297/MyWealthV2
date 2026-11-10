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
        string source) =>
        throw new NotImplementedException();

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return BaseCurrency;
        yield return QuoteCurrency;
        yield return Rate;
        yield return AsOfDate;
        yield return Source;
    }
}
