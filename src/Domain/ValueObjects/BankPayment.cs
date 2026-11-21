namespace MyWealthV2.Domain.ValueObjects;

/// <summary>The bank's details of a payment that a payout keeps (withdrawals R41): the value date, the reference sent,
/// the payee's name and the masked payee account.</summary>
public sealed record BankPayment(DateOnly ValueDate, string? Reference, string? PayeeName, string? PayeeAccount);
