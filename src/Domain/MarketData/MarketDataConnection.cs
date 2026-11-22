namespace MyWealthV2.Domain.MarketData;

/// <summary>
/// One configured market data connection (docs/features/simulated-instruments.md R1, R2, R4): a name, an adapter and a
/// symbol suffix. It serves every Instrument, of any Tenant, whose symbol ends with the suffix.
/// </summary>
public sealed record MarketDataConnection(string Name, string Adapter, string SymbolSuffix)
{
    public const int SuffixMinLetters = 2;
    public const int SuffixMaxLetters = 10;

    /// <summary>R4: the symbol ends with the suffix, compared without regard to case.</summary>
    public bool Serves(string? symbol) =>
        symbol is not null
        && symbol.Length > SymbolSuffix.Length
        && symbol.EndsWith(SymbolSuffix, StringComparison.OrdinalIgnoreCase);

    /// <summary>R2: a <c>.</c> followed by 2 to 10 letters or digits.</summary>
    public static bool IsValidSuffix(string? suffix) =>
        suffix is { Length: >= SuffixMinLetters + 1 and <= SuffixMaxLetters + 1 }
        && suffix[0] == '.'
        && suffix.Skip(1).All(char.IsAsciiLetterOrDigit);
}
