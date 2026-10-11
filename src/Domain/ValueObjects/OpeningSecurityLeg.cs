namespace MyWealthV2.Domain.ValueObjects;

public class OpeningSecurityLeg : ValueObject
{
    private OpeningSecurityLeg()
    {
    }
    
    public int InstrumentId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal Cost { get; private set; }
    public string QuoteCurrency { get; private set; } = string.Empty;
    
    public static OpeningSecurityLeg Create(int instrumentId, decimal quantity, decimal cost, string quoteCurrency)
    {
        if (instrumentId <= 0)
            throw new DomainException("Instrument is required.");
        if (quantity <= 0)
            throw new DomainException("Quantity must be greater than 0.");
        if (cost < 0)
            throw new DomainException("Cost must be greater than or equal to 0.");
        if (string.IsNullOrWhiteSpace(quoteCurrency) || quoteCurrency.Trim().Length != 3)
            throw new DomainException("Quote currency must be a 3-letter code.");

        return new OpeningSecurityLeg
        {
            InstrumentId = instrumentId,
            Quantity = quantity,
            Cost = cost,
            QuoteCurrency = quoteCurrency.Trim().ToUpperInvariant()
        };
    }
    
    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return InstrumentId;
        yield return Quantity;
        yield return Cost;
        yield return QuoteCurrency;
    }
}
