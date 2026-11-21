using MyWealthV2.Domain.ValueObjects;

namespace MyWealthV2.Domain.Entities;

/// <summary>The bank's details of a payout, on the payout and on its Reversal (withdrawals §5, R41, R42). Immutable.</summary>
public class TransactionPaymentDetail
{
    public const int ReferenceMaxLength = 18;
    public const int PayeeNameMaxLength = 140;
    public const int PayeeAccountMaxLength = 40;

    private TransactionPaymentDetail()
    {
    }

    public int TenantId { get; private set; }

    public int TransactionId { get; private set; }

    public DateOnly ValueDate { get; private set; }

    public string? Reference { get; private set; }

    public string? PayeeName { get; private set; }

    public string? PayeeAccount { get; private set; }

    internal static TransactionPaymentDetail Create(int tenantId, BankPayment payment)
    {
        ArgumentNullException.ThrowIfNull(payment);
        return new TransactionPaymentDetail
        {
            TenantId = tenantId,
            ValueDate = payment.ValueDate,
            Reference = Text(payment.Reference, ReferenceMaxLength, "Reference"),
            PayeeName = Text(payment.PayeeName, PayeeNameMaxLength, "Payee name"),
            PayeeAccount = Text(payment.PayeeAccount, PayeeAccountMaxLength, "Payee account")
        };
    }

    /// <summary>A new row with the same details, for the Reversal of a returned payout (R42).</summary>
    internal TransactionPaymentDetail Copy() => new()
    {
        TenantId = TenantId,
        ValueDate = ValueDate,
        Reference = Reference,
        PayeeName = PayeeName,
        PayeeAccount = PayeeAccount
    };

    private static string? Text(string? value, int maxLength, string label) =>
        value is { Length: > 0 } && value.Length > maxLength
            ? throw new DomainException($"{label} must be at most {maxLength} characters.")
            : value;
}
