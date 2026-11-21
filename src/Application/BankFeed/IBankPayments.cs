using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.BankFeed;

/// <summary>
/// The bank payments port (docs/features/withdrawals.md R58; ADR 0021 rules 12–14): ask the bank of a connection to
/// make one payment from a trust account. The instruction id is the idempotency key the bank sees on every attempt.
/// Infrastructure dispatches to the adapter of the connection. A failure comes back as a result, never as an
/// exception the caller must parse.
/// </summary>
public interface IBankPayments
{
    Task<BankPaymentResult> SendAsync(BankPaymentRequest request, CancellationToken cancellationToken);
}

/// <summary>The account the money goes to: the full number and the holder's name.</summary>
public sealed record BankPaymentPayee(string AccountNumber, string Name);

/// <summary>One payment from <paramref name="TrustAccountNumber"/>, built from stored data only (R58). It pays
/// <paramref name="Payee"/>, or sends back the receipt line <paramref name="ReturnOfOutsideId"/> to its payer
/// (bank-returns R6); exactly one of the two.</summary>
public sealed record BankPaymentRequest(
    string Connection,
    string TrustAccountNumber,
    Guid InstructionId,
    decimal Amount,
    string Currency,
    string? Reference,
    BankPaymentPayee? Payee,
    string? ReturnOfOutsideId = null);

/// <summary>The bank's answer to one call (R33): accepted with its payment id, rejected with a reason, or a failure
/// of the call itself.</summary>
public sealed class BankPaymentResult
{
    private BankPaymentResult(string? paymentId, PaymentRejectReason? rejectReason, BankFeedFailure? failure)
    {
        PaymentId = paymentId;
        RejectReason = rejectReason;
        Failure = failure;
    }

    public string? PaymentId { get; }

    public PaymentRejectReason? RejectReason { get; }

    public BankFeedFailure? Failure { get; }

    public static BankPaymentResult Accepted(string paymentId) => new(paymentId, null, null);

    public static BankPaymentResult Rejected(PaymentRejectReason reason) => new(null, reason, null);

    public static BankPaymentResult Failed(BankFeedFailure failure) => new(null, null, failure);
}
