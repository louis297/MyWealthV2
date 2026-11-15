namespace MyWealthV2.Domain.Entities;

public class TransactionCashLeg : BaseAuditableEntity
{
    private TransactionCashLeg()
    {
    }

    public int TenantId { get; private set; }

    public int TransactionId { get; private set; }

    public int AccountId { get; private set; }

    public CashLegRole Role { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    public byte[] RowVersion { get; private set; } = null!;

    /// <summary>The fee behind a <see cref="CashLegRole.Fee"/> leg; null on a <see cref="CashLegRole.Principal"/> leg.</summary>
    public TransactionFeeDetail? FeeDetail { get; private set; }

    internal static TransactionCashLeg Create(int tenantId, int accountId, decimal amount, string currency) =>
        new()
        {
            TenantId = tenantId,
            AccountId = accountId,
            Amount = amount,
            Currency = currency
        };

    /// <summary>A <see cref="CashLegRole.Fee"/> leg with its fee detail (trade-fees R18).</summary>
    internal static TransactionCashLeg CreateFee(
        int tenantId,
        int accountId,
        decimal amount,
        string currency,
        TransactionFeeDetail detail) =>
        new()
        {
            TenantId = tenantId,
            AccountId = accountId,
            Role = CashLegRole.Fee,
            Amount = amount,
            Currency = currency,
            FeeDetail = detail
        };
}
