namespace MyWealthV2.Domain.Enums;

/// <summary>Where a trade settlement is (docs/features/trade-settlement.md R10).</summary>
public enum TradeSettlementStatus
{
    /// <summary>The firm owes the broker; an outgoing payment is <c>Pending</c> or <c>Accepted</c>.</summary>
    AwaitingPayment = 0,

    /// <summary>The broker owes the firm; no statement line has matched yet.</summary>
    AwaitingReceipt = 1,

    /// <summary>The cash is on the bank statement, or the net was 0.</summary>
    Completed = 2,

    /// <summary>The bank refused or returned the payment, or the broker's account is not configured.</summary>
    PaymentFailed = 3,

    /// <summary>The broker's figures and the platform's differ (R9).</summary>
    Disputed = 4,

    /// <summary>A TenantAdmin ended it by hand with a note (R15).</summary>
    SetAside = 5
}
