using Microsoft.EntityFrameworkCore;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.MarketData;
using MyWealthV2.Domain.ValueObjects;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Data.Entities;

namespace MyWealthV2.Infrastructure.MarketData;

public sealed class InstrumentPriceStore(ApplicationDbContext db, TimeProvider time) : IInstrumentPriceStore, IMarketData
{
    public const string SourceName = "TwelveData";

    public Money? TryGetPrice(int instrumentId, DateTimeOffset? asOf = null)
    {
        var date = asOf is null
            ? DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime)
            : DateOnly.FromDateTime(asOf.Value.DateTime);
        var row = db.InstrumentPrices.AsNoTracking()
            .Where(price => price.InstrumentId == instrumentId && price.AsOfDate <= date)
            .OrderByDescending(price => price.AsOfDate)
            .Select(price => (decimal?)price.Price)
            .FirstOrDefault();
        if (row is null)
        {
            return null;
        }

        var quote = db.Instruments.IgnoreQueryFilters().AsNoTracking()
            .Where(instrument => instrument.Id == instrumentId)
            .Select(instrument => instrument.QuoteCurrency)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(quote))
        {
            return null;
        }

        return Money.Create(row.Value, quote);
    }

    public async Task UpsertAsync(
        IReadOnlyList<InstrumentPriceQuote> quotes,
        DateTimeOffset fetchedAt,
        CancellationToken cancellationToken = default)
    {
        if (quotes.Count == 0)
        {
            return;
        }

        var ids = quotes.Select(quote => quote.InstrumentId).Distinct().ToList();
        var existing = await db.InstrumentPrices
            .Where(row => ids.Contains(row.InstrumentId))
            .ToListAsync(cancellationToken);
        var rows = existing
            .Select(row => new InstrumentPriceRow(row.InstrumentId, row.Price, row.AsOfDate, row.FetchedAt))
            .ToList();
        foreach (var quote in quotes)
        {
            rows = InstrumentPriceBook.Upsert(
                rows, quote.InstrumentId, quote.Price, quote.AsOfDate, fetchedAt).ToList();
        }

        foreach (var row in rows)
        {
            var entity = existing.SingleOrDefault(candidate =>
                candidate.InstrumentId == row.InstrumentId && candidate.AsOfDate == row.AsOfDate);
            if (entity is null)
            {
                entity = new InstrumentPrice
                {
                    InstrumentId = row.InstrumentId,
                    AsOfDate = row.AsOfDate
                };
                db.InstrumentPrices.Add(entity);
                existing.Add(entity);
            }

            entity.Price = row.Price;
            entity.Source = SourceName;
            entity.FetchedAt = row.FetchedAt;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> HasAsOfDateForEveryActiveInstrumentAsync(
        IReadOnlyList<int> instrumentIds,
        DateOnly asOf,
        CancellationToken cancellationToken = default)
    {
        var ids = instrumentIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return true;
        }

        var stored = await db.InstrumentPrices.AsNoTracking()
            .Where(row => row.AsOfDate == asOf && ids.Contains(row.InstrumentId))
            .Select(row => row.InstrumentId)
            .Distinct()
            .CountAsync(cancellationToken);
        return stored == ids.Count;
    }

    public async Task<IReadOnlyDictionary<int, DateTimeOffset>> LatestFetchedAtByInstrumentAsync(
        IReadOnlyList<int> instrumentIds,
        CancellationToken cancellationToken = default)
    {
        var ids = instrumentIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<int, DateTimeOffset>();
        }

        var rows = await db.InstrumentPrices.AsNoTracking()
            .Where(row => ids.Contains(row.InstrumentId))
            .GroupBy(row => row.InstrumentId)
            .Select(group => new { InstrumentId = group.Key, FetchedAt = group.Max(row => row.FetchedAt) })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(row => row.InstrumentId, row => row.FetchedAt);
    }

    public async Task NoteFetchedAsync(
        IReadOnlyList<int> instrumentIds,
        DateTimeOffset fetchedAt,
        CancellationToken cancellationToken = default)
    {
        var ids = instrumentIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return;
        }

        var rows = await db.InstrumentPrices
            .Where(row => ids.Contains(row.InstrumentId))
            .ToListAsync(cancellationToken);
        foreach (var group in rows.GroupBy(row => row.InstrumentId))
        {
            var latest = group.OrderByDescending(row => row.AsOfDate).First();
            latest.FetchedAt = fetchedAt;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
