namespace MyWealthV2.Domain.Entities;

/// <summary>
/// One payment the platform asks a bank to make from a client money account (docs/features/withdrawals.md R31). The
/// instruction's PublicId is the idempotency key the bank sees on every attempt (ADR 0021 rule 13). In this slice
/// every outgoing payment pays a withdrawal.
/// </summary>
public class OutgoingPayment : BaseAuditableEntity
{
    public const int BankPaymentIdMaxLength = 100;

    private OutgoingPayment()
    {
    }

    public Guid PublicId { get; private set; }

    public int TenantId { get; private set; }

    public int ClientMoneyAccountId { get; private set; }

    public string Connection { get; private set; } = string.Empty;

    public Guid InstructionPublicId { get; private set; }

    public int WithdrawalId { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    public string? Reference { get; private set; }

    public OutgoingPaymentStatus Status { get; private set; }

    public int Attempts { get; private set; }

    public DateTimeOffset? LastAttemptAt { get; private set; }

    public BankFeedFailure? LastFailure { get; private set; }

    public string? BankPaymentId { get; private set; }

    public DateTimeOffset? AcceptedAt { get; private set; }

    public PaymentRejectReason? RejectReason { get; private set; }

    public byte[] RowVersion { get; private set; } = null!;

    public static OutgoingPayment ForWithdrawal(Withdrawal withdrawal, ClientMoneyAccount clientMoney) =>
        throw new NotImplementedException();

    public void RecordAttempt(DateTimeOffset at) => throw new NotImplementedException();

    public void Accept(string bankPaymentId, DateTimeOffset at) => throw new NotImplementedException();

    public void Reject(PaymentRejectReason reason) => throw new NotImplementedException();

    public void RecordFailure(BankFeedFailure failure) => throw new NotImplementedException();

    public void Cancel() => throw new NotImplementedException();

    public void MarkPaid() => throw new NotImplementedException();

    public void MarkReturned() => throw new NotImplementedException();
}
