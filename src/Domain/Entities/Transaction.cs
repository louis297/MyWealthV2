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
        EnsureSign(type, amount);
        if (type == TransactionType.Opening && existingTransactionCount != 0)
        {
            throw new DomainException("Opening is allowed only while the account has no other transaction.");
        }

        if (type == TransactionType.CloseOut && amount != -currentCashSum)
        {
            throw new DomainException("CloseOut must bring the cash balance to zero.");
        }

        if (currentCashSum + amount < 0)
        {
            throw new DomainException("Cash balance cannot become negative.");
        }

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
        transaction.AddDomainEvent(new TransactionPosted(transaction));
        return transaction;
    }

    public Transaction Reverse(DateTimeOffset bookedAt, decimal currentCashSum)
    {
        if (Type == TransactionType.Reversal)
        {
            throw new DomainException("A reversal cannot be reversed.");
        }

        if (CashLeg is null)
        {
            throw new DomainException("A transaction has no cash leg to reverse.");
        }

        var opposite = -CashLeg.Amount;
        if (currentCashSum + opposite < 0)
        {
            throw new DomainException("Cash balance cannot become negative.");
        }

        var reversal = new Transaction
        {
            TenantId = TenantId,
            AccountId = AccountId,
            Type = TransactionType.Reversal,
            BookedAt = bookedAt,
            PublicId = Guid.NewGuid(),
            OriginalTransactionId = Id,
            CashLeg = TransactionCashLeg.Create(opposite, CashLeg.Currency)
        };
        reversal.AddDomainEvent(new TransactionReversed(reversal));
        return reversal;
    }

    private static void EnsureSign(TransactionType type, decimal amount)
    {
        var invalid = type switch
        {
            TransactionType.TransferIn or TransactionType.Opening => amount <= 0,
            TransactionType.TransferOut or TransactionType.CloseOut => amount >= 0,
            TransactionType.Interest => amount == 0,
            _ => true
        };

        if (invalid)
        {
            throw new DomainException("Amount sign is not valid for this transaction type.");
        }
    }
}
