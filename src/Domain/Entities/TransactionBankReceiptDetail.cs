using MyWealthV2.Domain.ValueObjects;

namespace MyWealthV2.Domain.Entities;

/// <summary>The bank's details of a deposit, on the deposit and on its Reversal (deposit-matching §5, R32, R33).
/// Immutable.</summary>
public class TransactionBankReceiptDetail
{
    public const int FreeTextMaxLength = 140;
    public const int PayerAccountMaxLength = 40;

    private TransactionBankReceiptDetail()
    {
    }

    public int TenantId { get; private set; }

    public int TransactionId { get; private set; }

    public DateOnly ValueDate { get; private set; }

    public string? Reference { get; private set; }

    public string? PayerName { get; private set; }

    public string? PayerAccount { get; private set; }

    internal static TransactionBankReceiptDetail Create(int tenantId, BankReceipt receipt) =>
        throw new NotImplementedException();

    /// <summary>A new row with the same details, for the Reversal (R33).</summary>
    internal TransactionBankReceiptDetail Copy() => throw new NotImplementedException();
}
