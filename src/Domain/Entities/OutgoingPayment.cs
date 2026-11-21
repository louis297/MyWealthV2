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

    /// <summary>R29: the payment of an approved withdrawal, <c>Pending</c>, from the client money account.</summary>
    public static OutgoingPayment ForWithdrawal(Withdrawal withdrawal, ClientMoneyAccount clientMoney)
    {
        if (withdrawal.Id == 0)
        {
            throw new DomainException("The withdrawal must be saved first.");
        }

        if (clientMoney.TenantId != withdrawal.TenantId
            || !string.Equals(clientMoney.Currency.Trim(), withdrawal.Currency, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException("The client money account must be the withdrawal's tenant's, in its currency.");
        }

        return new OutgoingPayment
        {
            TenantId = withdrawal.TenantId,
            ClientMoneyAccountId = clientMoney.Id,
            Connection = clientMoney.BankConnection,
            InstructionPublicId = withdrawal.PublicId,
            WithdrawalId = withdrawal.Id,
            Amount = withdrawal.Amount,
            Currency = withdrawal.Currency,
            Reference = withdrawal.Reference,
            Status = OutgoingPaymentStatus.Pending,
            PublicId = Guid.NewGuid()
        };
    }

    /// <summary>R32: an attempt is recorded and committed before the call.</summary>
    public void RecordAttempt(DateTimeOffset at)
    {
        EnsureStatus(OutgoingPaymentStatus.Pending);
        Attempts++;
        LastAttemptAt = at;
    }

    /// <summary>R33: the bank accepted it and gave its payment id.</summary>
    public void Accept(string bankPaymentId, DateTimeOffset at)
    {
        EnsureStatus(OutgoingPaymentStatus.Pending);
        if (string.IsNullOrWhiteSpace(bankPaymentId) || bankPaymentId.Length > BankPaymentIdMaxLength)
        {
            throw new DomainException($"Bank payment id must be 1 to {BankPaymentIdMaxLength} characters.");
        }

        Status = OutgoingPaymentStatus.Accepted;
        BankPaymentId = bankPaymentId;
        AcceptedAt = at;
        LastFailure = null;
    }

    /// <summary>R33: the bank refused it.</summary>
    public void Reject(PaymentRejectReason reason)
    {
        EnsureStatus(OutgoingPaymentStatus.Pending);
        Status = OutgoingPaymentStatus.Rejected;
        RejectReason = reason;
        LastFailure = null;
    }

    /// <summary>R33: the call failed; the payment stays <c>Pending</c> and is sent again with the same id.</summary>
    public void RecordFailure(BankFeedFailure failure)
    {
        EnsureStatus(OutgoingPaymentStatus.Pending);
        if (failure == BankFeedFailure.InvalidLine)
        {
            throw new DomainException("A payment call fails as Unreachable, Timeout, Unauthorized, Unavailable, UnknownAccount or BadResponse.");
        }

        LastFailure = failure;
    }

    /// <summary>R30: only before the first attempt, since after it the bank may have the payment.</summary>
    public void Cancel()
    {
        EnsureStatus(OutgoingPaymentStatus.Pending);
        if (Attempts > 0)
        {
            throw new OutgoingPaymentAttemptedException();
        }

        Status = OutgoingPaymentStatus.Cancelled;
    }

    /// <summary>R40: the statement showed it, also when the accepted answer was lost.</summary>
    public void MarkPaid()
    {
        EnsureStatus(OutgoingPaymentStatus.Pending, OutgoingPaymentStatus.Accepted);
        Status = OutgoingPaymentStatus.Paid;
    }

    /// <summary>R42: the payee's bank sent it back.</summary>
    public void MarkReturned()
    {
        EnsureStatus(OutgoingPaymentStatus.Paid);
        Status = OutgoingPaymentStatus.Returned;
    }

    private void EnsureStatus(params OutgoingPaymentStatus[] from)
    {
        if (!from.Contains(Status))
        {
            throw new DomainException($"The outgoing payment is {Status}.");
        }
    }
}
