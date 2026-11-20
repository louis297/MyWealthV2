namespace MyWealthV2.Domain.Exceptions;

/// <summary>The outgoing payment has a send attempt, so the bank may have it and it can no longer be cancelled
/// (withdrawals R30).</summary>
public class OutgoingPaymentAttemptedException()
    : DomainException("The bank has been asked to pay this withdrawal, so it can no longer be cancelled.");
