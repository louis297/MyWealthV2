namespace MyWealthV2.Domain.Enums;

/// <summary>What an outgoing payment pays (docs/features/bank-returns.md §5).</summary>
public enum OutgoingPaymentKind
{
    /// <summary>A withdrawal to a nominated bank account (withdrawals R31).</summary>
    Withdrawal = 0,

    /// <summary>A receipt sent back to its payer (bank-returns R5).</summary>
    ReceiptReturn = 1,

    /// <summary>The firm's net of a trade settlement, paid to the broker (trade-settlement R12).</summary>
    TradeSettlement = 2
}
