namespace MyWealthV2.Application.MarketData;

public readonly record struct SourcedInstrumentPrice(decimal Price, DateOnly AsOfDate, string Currency);
