namespace MyWealthV2.Domain.Entities;

public class Transaction : BaseAuditableEntity
{
    private Transaction()
    {
    }

    public Guid PublicId { get; private set; }

    public int TenantId { get; private set; }

    public int AccountId { get; private set; }

    public TransactionType Type { get; private set; }

    public DateTimeOffset BookedAt { get; private set; }

    public string? Memo { get; private set; }

    public string? Reference { get; private set; }

    public int? OriginalTransactionId { get; private set; }

    public byte[] RowVersion { get; private set; } = null!;

    public TransactionCashLeg? CashLeg { get; private set; }

    public static Transaction Post(
        int tenantId,
        int accountId,
        TransactionType type,
        decimal amount,
        string accountCurrency,
        DateTimeOffset bookedAt,
        string? memo,
        string? reference,
        decimal currentCashSum,
        int existingTransactionCount)
    {
        _ = (tenantId, accountId, type, amount, accountCurrency, bookedAt, memo, reference, currentCashSum, existingTransactionCount);
        throw new DomainException("Not posted.");
    }
}
