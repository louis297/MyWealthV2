namespace MyWealthV2.Domain.ValueObjects;

/// <summary>The bank's details of a receipt that a deposit keeps (deposit-matching R32): the value date, the
/// reference text, the payer's name and the masked payer account.</summary>
public sealed record BankReceipt(DateOnly ValueDate, string? Reference, string? PayerName, string? PayerAccount);
