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
    public bool Serves(string? symbol) => false;

    /// <summary>R2: a <c>.</c> followed by 2 to 10 letters or digits.</summary>
    public static bool IsValidSuffix(string? suffix) => false;
}
