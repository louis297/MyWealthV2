namespace MyWealthV2.Application.Common.Exceptions;

public sealed class FxRateNotFoundException : Exception
{
    public FxRateNotFoundException(string baseCurrency, string quoteCurrency, DateOnly asOf)
        : base($"No FX rate for {baseCurrency}/{quoteCurrency} on {asOf:yyyy-MM-dd}.")
    {
        BaseCurrency = baseCurrency;
        QuoteCurrency = quoteCurrency;
        AsOf = asOf;
    }

    public string BaseCurrency { get; }

    public string QuoteCurrency { get; }

    public DateOnly AsOf { get; }
}
