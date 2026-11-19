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

    /// <summary>The 22 letters of <see cref="Alphabet"/>: alphabet positions 10 to 31.</summary>
    private const int FirstLetter = 10;

    /// <summary>Draws a new reference: a letter, seven symbols, then the check symbol (R15).</summary>
    public static DepositReference Generate(IRandomSymbols random)
    {
        ArgumentNullException.ThrowIfNull(random);
        var chars = new char[Length];
        chars[0] = Alphabet[FirstLetter + random.Next(Alphabet.Length - FirstLetter)];
        for (var i = 1; i < Length - 1; i++)
        {
            chars[i] = Alphabet[random.Next(Alphabet.Length)];
        }

        chars[Length - 1] = CheckSymbol(chars.AsSpan(0, Length - 1));
        return new DepositReference(new string(chars));
    }

    public static bool TryParse(string? value, out DepositReference reference)
    {
        if (!IsValid(value))
        {
            reference = null!;
            return false;
        }

        reference = new DepositReference(value!);
        return true;
    }

    /// <summary>
    /// True when <paramref name="value"/> is nine upper-case alphabet symbols, the first a letter, and its check
    /// symbol is right (Luhn mod 32 over <see cref="Alphabet"/>, spec §5).
    /// </summary>
    public static bool IsValid(string? value)
    {
        if (value is null || value.Length != Length || Alphabet.IndexOf(value[0]) < FirstLetter)
        {
            return false;
        }

        var factor = 1;
        var sum = 0;
        for (var i = Length - 1; i >= 0; i--)
        {
            var index = Alphabet.IndexOf(value[i]);
            if (index < 0)
            {
                return false;
            }

            sum += Addend(factor, index);
            factor = factor == 2 ? 1 : 2;
        }

        return sum % Alphabet.Length == 0;
    }

    private static char CheckSymbol(ReadOnlySpan<char> payload)
    {
        var factor = 2;
        var sum = 0;
        for (var i = payload.Length - 1; i >= 0; i--)
        {
            sum += Addend(factor, Alphabet.IndexOf(payload[i]));
            factor = factor == 2 ? 1 : 2;
        }

        return Alphabet[(Alphabet.Length - sum % Alphabet.Length) % Alphabet.Length];
    }

    private static int Addend(int factor, int index)
    {
        var product = factor * index;
        return product / Alphabet.Length + product % Alphabet.Length;
    }

    public override string ToString() => Value;

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
