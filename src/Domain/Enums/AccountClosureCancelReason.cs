namespace MyWealthV2.Domain.Enums;

/// <summary>Why an account closure ended without closing the Account (account-closure R19, R20).</summary>
public enum AccountClosureCancelReason
{
    WithdrawalRejected = 0,
    WithdrawalCancelled = 1,
    PaymentFailed = 2,
    NotClosable = 3
}
