using MyWealthV2.Domain.ValueObjects;

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
        var currency = Money.Create(0m, accountCurrency).Currency;
        var transaction = new Transaction
        {
            TenantId = tenantId,
            AccountId = accountId,
            Type = type,
            BookedAt = bookedAt,
            Memo = memo,
            Reference = reference,
            PublicId = Guid.NewGuid(),
            CashLeg = TransactionCashLeg.Create(amount, currency)
        };
        _ = currentCashSum;
        _ = existingTransactionCount;
        transaction.AddDomainEvent(new TransactionPosted(transaction));
        return transaction;
    }
}
