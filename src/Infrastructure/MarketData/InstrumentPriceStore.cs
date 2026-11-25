using Microsoft.EntityFrameworkCore;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.MarketData;
using MyWealthV2.Domain.MarketData;
using MyWealthV2.Domain.ValueObjects;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Data.Entities;

namespace MyWealthV2.Infrastructure.MarketData;

public sealed class InstrumentPriceStore(ApplicationDbContext db, TimeProvider time) : IInstrumentPriceStore, IMarketData
{
    public const string SourceName = "EODHD";

    public Money? TryGetPrice(int instrumentId, DateTimeOffset? asOf = null)
    {
        var date = asOf is null
            ? DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime)
            : DateOnly.FromDateTime(asOf.Value.UtcDateTime);
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

    public InstrumentPriceReading? TryGetPriceDetail(int instrumentId, DateTimeOffset? asOf = null) => null;

    public Task<PagedList<StoredInstrumentPrice>> ListPricesAsync(
        int instrumentId,
        DateOnly? from,
        DateOnly? to,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new PagedList<StoredInstrumentPrice> { Items = [], Page = page, PageSize = pageSize, TotalCount = 0 });

    public Task UpsertAsync(
        IReadOnlyList<InstrumentPriceQuote> quotes,
        DateTimeOffset fetchedAt,
        CancellationToken cancellationToken = default) =>
        UpsertAsync(quotes, fetchedAt, SourceName, cancellationToken);

    public async Task UpsertAsync(
        IReadOnlyList<InstrumentPriceQuote> quotes,
        DateTimeOffset fetchedAt,
        string source,
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

        // Market data history R4–R6: a final row is never updated; a missing one may still be inserted.
        var today = DateOnly.FromDateTime(fetchedAt.UtcDateTime);
        var stored = existing.Select(row => (row.InstrumentId, row.AsOfDate)).ToHashSet();
        quotes = quotes
            .Where(quote => !stored.Contains((quote.InstrumentId, quote.AsOfDate))
                            || !MarketDataRow.IsFinal(quote.AsOfDate, today))
            .ToList();
        var rows = existing
            .Select(row => new InstrumentPriceRow(row.InstrumentId, row.Price, row.AsOfDate, row.FetchedAt))
            .ToList();
        foreach (var quote in quotes)
        {
            rows = InstrumentPriceBook.Upsert(
                rows, quote.InstrumentId, quote.Price, quote.AsOfDate, fetchedAt).ToList();
        }

        var quoted = quotes.Select(quote => (quote.InstrumentId, quote.AsOfDate)).ToHashSet();
        foreach (var row in rows)
        {
            var entity = existing.SingleOrDefault(candidate =>
                candidate.InstrumentId == row.InstrumentId && candidate.AsOfDate == row.AsOfDate);
            if (!quoted.Contains((row.InstrumentId, row.AsOfDate)))
            {
                // A row this run did not quote keeps its own source.
                continue;
            }

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
            entity.Source = source;
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

    public async Task<DateOnly?> LatestDateAsync(int instrumentId, DateOnly onOrBefore, CancellationToken cancellationToken = default) =>
        await db.InstrumentPrices.AsNoTracking()
            .Where(row => row.InstrumentId == instrumentId && row.AsOfDate <= onOrBefore)
            .OrderByDescending(row => row.AsOfDate)
            .Select(row => (DateOnly?)row.AsOfDate)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<bool> HasPriceFromAsync(int instrumentId, DateOnly from, CancellationToken cancellationToken = default) =>
        db.InstrumentPrices.AsNoTracking().AnyAsync(row => row.InstrumentId == instrumentId && row.AsOfDate >= from, cancellationToken);
}
