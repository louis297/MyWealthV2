namespace MyWealthV2.Domain.Entities;

public class TransactionCashLeg : BaseAuditableEntity
{
    private TransactionCashLeg()
    {
    }

    public int TransactionId { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    public byte[] RowVersion { get; private set; } = null!;
}
