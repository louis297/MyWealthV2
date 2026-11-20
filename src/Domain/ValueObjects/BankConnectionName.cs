namespace MyWealthV2.Domain.ValueObjects;

/// <summary>
/// The name of a configured bank connection (docs/features/deposit-matching.md R1): 1–50 characters, a lower-case
/// letter followed by lower-case letters, digits and hyphens.
/// </summary>
public static class BankConnectionName
{
    public const int MaxLength = 50;

    public static bool IsValid(string? name) =>
        name is { Length: >= 1 and <= MaxLength }
        && name[0] is >= 'a' and <= 'z'
        && name.All(ch => ch is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');
}
