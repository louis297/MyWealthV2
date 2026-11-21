namespace MyWealthV2.Domain.Enums;

/// <summary>What an outgoing payment pays (docs/features/bank-returns.md §5).</summary>
public enum OutgoingPaymentKind
{
    /// <summary>A withdrawal to a nominated bank account (withdrawals R31).</summary>
    Withdrawal = 0,

    /// <summary>A receipt sent back to its payer (bank-returns R5).</summary>
    ReceiptReturn = 1
}
