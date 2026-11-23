namespace MyWealthV2.Domain.Enums;

/// <summary>Why a trade settlement is <c>PaymentFailed</c> (trade-settlement R12, R17, R20).</summary>
public enum TradeSettlementFailure
{
    /// <summary>The bank refused the payment.</summary>
    PaymentRejected = 0,

    /// <summary>The broker's bank sent the payment back.</summary>
    PaymentReturned = 1,

    /// <summary>The broker connection has no bank account in the settlement's currency.</summary>
    NoBrokerAccount = 2
}
