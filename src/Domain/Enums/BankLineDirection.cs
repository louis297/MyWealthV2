namespace MyWealthV2.Domain.Enums;

/// <summary>Money into the trust account (<see cref="Credit"/>) or out of it (<see cref="Debit"/>) (deposit-matching R10).</summary>
public enum BankLineDirection
{
    Credit = 0,
    Debit = 1
}
