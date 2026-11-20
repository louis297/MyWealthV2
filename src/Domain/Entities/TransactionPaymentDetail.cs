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

    internal static TransactionPaymentDetail Create(int tenantId, BankPayment payment) => throw new NotImplementedException();

    internal TransactionPaymentDetail Copy() => throw new NotImplementedException();
}
