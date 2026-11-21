namespace MyWealthV2.Domain.Enums;

/// <summary>Why a bank refused an outgoing payment (withdrawals R34). A reason the adapter does not know is
/// <see cref="Refused"/>.</summary>
public enum PaymentRejectReason
{
    InvalidAccount = 0,
    AccountClosed = 1,
    InsufficientFunds = 2,
    Refused = 3
}
