namespace MyWealthV2.Infrastructure.MarketData;

public static class EodhdSymbols
{
    private static readonly Dictionary<(string Symbol, string Quote), string> Suffixes = new()
    {
        [("AIA", "NZD")] = "AIA.NZ",
        [("FPH", "NZD")] = "FPH.NZ",
        [("MEL", "NZD")] = "MEL.NZ",
        [("BHP", "AUD")] = "BHP.AU",
        [("CBA", "AUD")] = "CBA.AU",
        [("WOW", "AUD")] = "WOW.AU",
        [("SAP", "EUR")] = "SAP.XETRA",
        [("AIR", "EUR")] = "AIR.PA",
        [("OR", "EUR")] = "OR.PA",
        [("SHEL", "GBP")] = "SHEL.LSE",
        [("HSBA", "GBP")] = "HSBA.LSE",
        [("ULVR", "GBP")] = "ULVR.LSE",
        [("7203", "JPY")] = "7203.TSE",
        [("6758", "JPY")] = "6758.TSE",
        [("9984", "JPY")] = "9984.TSE",
        [("VTI", "USD")] = "VTI.US",
        [("AAPL", "USD")] = "AAPL.US",
        [("MSFT", "USD")] = "MSFT.US"
    };

    public static string For(string symbol, string quoteCurrency)
    {
        var key = (symbol.Trim().ToUpperInvariant(), quoteCurrency.Trim().ToUpperInvariant());
        return Suffixes.TryGetValue(key, out var provider) ? provider : key.Item1;
    }
}
