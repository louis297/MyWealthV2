namespace MyWealthV2.Domain.ValueObjects;

/// <summary>
/// Keeps only the last four letters or digits of a bank account number (a payer, a payee or a nominated bank account; withdrawals R25), as <c>****1234</c>; fewer than five
/// gives <c>****</c> (deposit-matching R11). The full number is never stored.
/// </summary>
public static class AccountNumberMask
{
    private const string Prefix = "****";

    public static string? Mask(string? payerAccount)
    {
        if (string.IsNullOrWhiteSpace(payerAccount))
        {
            return null;
        }

        var symbols = payerAccount.Where(char.IsLetterOrDigit).ToArray();
        return symbols.Length < 5 ? Prefix : Prefix + new string(symbols[^4..]);
    }
}
