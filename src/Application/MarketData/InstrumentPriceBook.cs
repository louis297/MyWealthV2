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
        InstrumentPriceRow? chosen = null;
        foreach (var row in rows)
        {
            if (row.InstrumentId != instrumentId || row.AsOfDate > asOf)
            {
                continue;
            }

            if (chosen is null || row.AsOfDate > chosen.Value.AsOfDate)
            {
                chosen = row;
            }
        }

        return chosen?.Price;
    }

    public static IReadOnlyList<InstrumentPriceRow> Upsert(
        IReadOnlyList<InstrumentPriceRow> existing,
        int instrumentId,
        decimal price,
        DateOnly asOfDate,
        DateTimeOffset fetchedAt)
    {
        var updated = new List<InstrumentPriceRow>(existing.Count + 1);
        var replaced = false;
        foreach (var row in existing)
        {
            if (row.InstrumentId == instrumentId && row.AsOfDate == asOfDate)
            {
                updated.Add(new InstrumentPriceRow(instrumentId, price, asOfDate, fetchedAt));
                replaced = true;
            }
            else
            {
                updated.Add(row);
            }
        }

        if (!replaced)
        {
            updated.Add(new InstrumentPriceRow(instrumentId, price, asOfDate, fetchedAt));
        }

        return updated;
    }
}
