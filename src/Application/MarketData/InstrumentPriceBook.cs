namespace MyWealthV2.Application.MarketData;

public readonly record struct InstrumentPriceRow(
    int InstrumentId,
    decimal Price,
    DateOnly AsOfDate,
    DateTimeOffset FetchedAt);

public static class InstrumentPriceBook
{
    public static decimal? Select(
        int instrumentId,
        DateOnly asOf,
        IReadOnlyList<InstrumentPriceRow> rows)
    {
        _ = instrumentId;
        _ = asOf;
        _ = rows;
        return 0m;
    }

    public static IReadOnlyList<InstrumentPriceRow> Upsert(
        IReadOnlyList<InstrumentPriceRow> existing,
        int instrumentId,
        decimal price,
        DateOnly asOfDate,
        DateTimeOffset fetchedAt)
    {
        _ = instrumentId;
        _ = price;
        _ = asOfDate;
        _ = fetchedAt;
        return existing;
    }
}
