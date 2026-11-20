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

    internal static TransactionBankReceiptDetail Create(int tenantId, BankReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        return new TransactionBankReceiptDetail
        {
            TenantId = tenantId,
            ValueDate = receipt.ValueDate,
            Reference = Text(receipt.Reference, FreeTextMaxLength, "Reference"),
            PayerName = Text(receipt.PayerName, FreeTextMaxLength, "Payer name"),
            PayerAccount = Text(receipt.PayerAccount, PayerAccountMaxLength, "Payer account")
        };
    }

    /// <summary>A new row with the same details, for the Reversal (R33).</summary>
    internal TransactionBankReceiptDetail Copy() => new()
    {
        TenantId = TenantId,
        ValueDate = ValueDate,
        Reference = Reference,
        PayerName = PayerName,
        PayerAccount = PayerAccount
    };

    private static string? Text(string? value, int maxLength, string label) =>
        value is { Length: > 0 } && value.Length > maxLength
            ? throw new DomainException($"{label} must be at most {maxLength} characters.")
            : value;
}
