namespace MyWealthV2.Infrastructure.MarketData;

public static class TwelveDataSymbols
{
    private static readonly Dictionary<(string Symbol, string Quote), string> Suffixes = new()
    {
        [("AIA", "NZD")] = "AIA:NZX",
        [("FPH", "NZD")] = "FPH:NZX",
        [("MEL", "NZD")] = "MEL:NZX",
        [("BHP", "AUD")] = "BHP:ASX",
        [("CBA", "AUD")] = "CBA:ASX",
        [("WOW", "AUD")] = "WOW:ASX",
        [("SAP", "EUR")] = "SAP:XETR",
        [("AIR", "EUR")] = "AIR:EPA",
        [("OR", "EUR")] = "OR:EPA",
        [("SHEL", "GBP")] = "SHEL:LSE",
        [("HSBA", "GBP")] = "HSBA:LSE",
        [("ULVR", "GBP")] = "ULVR:LSE",
        [("7203", "JPY")] = "7203:TSE",
        [("6758", "JPY")] = "6758:TSE",
        [("9984", "JPY")] = "9984:TSE"
    };

    public static string For(string symbol, string quoteCurrency)
    {
        var key = (symbol.Trim().ToUpperInvariant(), quoteCurrency.Trim().ToUpperInvariant());
        return Suffixes.TryGetValue(key, out var provider) ? provider : key.Item1;
    }
}
