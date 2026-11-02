namespace MyWealthV2.Application.Fx;

public readonly record struct FxRateQuote(
    string BaseCurrency,
    string QuoteCurrency,
    decimal Rate,
    DateOnly AsOfDate);
