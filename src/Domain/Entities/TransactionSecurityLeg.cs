namespace MyWealthV2.Domain.Entities;

public class TransactionSecurityLeg : BaseAuditableEntity
{
    private TransactionSecurityLeg()
    {
    }

    public int TransactionId { get; private set; }

    public int InstrumentId { get; private set; }

    public decimal Quantity { get; private set; }

    public decimal CostAmount { get; private set; }

    public string CostCurrency { get; private set; } = string.Empty;

    public byte[] RowVersion { get; private set; } = null!;
}
