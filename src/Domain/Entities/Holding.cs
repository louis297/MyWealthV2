namespace MyWealthV2.Domain.Entities;

public class Holding : BaseAuditableEntity
{
    private Holding()
    {
    }

    public Guid PublicId { get; private set; }

    public int TenantId { get; private set; }

    public int AccountId { get; private set; }

    public int InstrumentId { get; private set; }

    public decimal Quantity { get; private set; }

    public decimal CostAmount { get; private set; }

    public string CostCurrency { get; private set; } = string.Empty;

    public byte[] RowVersion { get; private set; } = null!;

    public static Holding Create(
        int tenantId,
        int accountId,
        int instrumentId,
        decimal quantity,
        decimal costAmount,
        string costCurrency)
    {
        if (quantity <= 0)
        {
            throw new DomainException("Holding quantity must be greater than 0.");
        }

        if (costAmount < 0)
        {
            throw new DomainException("Holding cost cannot be negative.");
        }

        return new Holding
        {
            PublicId = Guid.NewGuid(),
            TenantId = tenantId,
            AccountId = accountId,
            InstrumentId = instrumentId,
            Quantity = quantity,
            CostAmount = costAmount,
            CostCurrency = NormaliseCurrency(costCurrency)
        };
    }

    public bool ReplaceTotals(decimal quantity, decimal costAmount, string costCurrency)
    {
        if (quantity < 0)
        {
            throw new DomainException("Holding quantity cannot become negative.");
        }

        if (quantity == 0)
        {
            return true;
        }

        if (costAmount < 0)
        {
            throw new DomainException("Holding cost cannot be negative.");
        }

        Quantity = quantity;
        CostAmount = costAmount;
        CostCurrency = NormaliseCurrency(costCurrency);
        return false;
    }

    private static string NormaliseCurrency(string costCurrency)
    {
        var currency = costCurrency.Trim().ToUpperInvariant();
        if (currency.Length != 3)
        {
            throw new DomainException("Cost currency must be a 3-letter code.");
        }

        return currency;
    }
}
