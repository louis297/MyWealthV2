namespace MyWealthV2.Infrastructure.MarketData;

public static class TwelveDataSymbols
{
    public static string For(string symbol, string quoteCurrency)
    {
        _ = quoteCurrency;
        return symbol.Trim().ToUpperInvariant();
    }
}
