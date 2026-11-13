namespace MyWealthV2.Domain.Entities;

public class TransactionCashLeg : BaseAuditableEntity
{
    private TransactionCashLeg()
    {
    }

    public int TenantId { get; private set; }

    public int TransactionId { get; private set; }

    public int AccountId { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    public byte[] RowVersion { get; private set; } = null!;

    internal static TransactionCashLeg Create(int tenantId, int accountId, decimal amount, string currency) =>
        new()
        {
            TenantId = tenantId,
            AccountId = accountId,
            Amount = amount,
            Currency = currency
        };
}
