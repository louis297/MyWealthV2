namespace MyWealthV2.Domain.Enums;

/// <summary>How a statement line was matched: by its deposit reference, by a TenantAdmin (deposit-matching R17, R26),
/// as the payout of a withdrawal, or as a returned payout (withdrawals R40, R42).</summary>
public enum StatementLineMatchMethod
{
    Reference = 0,
    Manual = 1,
    Payment = 2,
    PaymentReturn = 3
}
