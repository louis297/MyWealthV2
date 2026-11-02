namespace MyWealthV2.Application.MarketData;

public readonly record struct InstrumentPriceQuote(
    int InstrumentId,
    decimal Price,
    DateOnly AsOfDate);
