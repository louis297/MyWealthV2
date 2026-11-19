namespace MyWealthV2.Domain.ValueObjects;

/// <summary>
/// The short code that identifies one Custodial account in a bank payment (docs/features/client-money-accounts.md
/// R15): nine characters from a 32-symbol alphabet, the first a letter, the ninth a Luhn mod 32 check symbol.
/// </summary>
public sealed class DepositReference : ValueObject
{
    public const int Length = 9;

    /// <summary>The 32 symbols, without I, L, O and U.</summary>
    public const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    private DepositReference(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static DepositReference Generate(IRandomSymbols random) => throw new NotImplementedException();

    public static bool TryParse(string? value, out DepositReference reference) => throw new NotImplementedException();

    public static bool IsValid(string? value) => throw new NotImplementedException();

    public override string ToString() => Value;

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
