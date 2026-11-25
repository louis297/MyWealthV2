using Microsoft.EntityFrameworkCore;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Fx;
using MyWealthV2.Domain.MarketData;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Data.Entities;

namespace MyWealthV2.Infrastructure.MarketData;

public sealed class FxRateStore(ApplicationDbContext db, TimeProvider time) : IFxRate, IFxRateStore
{
    public async Task<decimal> GetRateAsync(
        string from,
        string to,
        DateTimeOffset? asOf = null,
        CancellationToken cancellationToken = default)
    {
        var source = from.Trim().ToUpperInvariant();
        var target = to.Trim().ToUpperInvariant();
        var date = asOf is null
            ? DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime)
            : DateOnly.FromDateTime(asOf.Value.UtcDateTime);

        var rows = await db.FxRates.AsNoTracking()
            .Where(row => row.BaseCurrency == source && row.QuoteCurrency == target)
            .Select(row => new FxRateQuote(row.BaseCurrency, row.QuoteCurrency, row.Rate, row.AsOfDate))
            .ToListAsync(cancellationToken);

        return FxRateSelector.Select(source, target, date, rows);
    }

    public async Task<FxRateReading> GetRateDetailAsync(
        string from,
        string to,
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default)
    {
        var source = from.Trim().ToUpperInvariant();
        var target = to.Trim().ToUpperInvariant();
        var date = DateOnly.FromDateTime(asOf.UtcDateTime);

        var row = await db.FxRates.AsNoTracking()
            .Where(rate => rate.BaseCurrency == source && rate.QuoteCurrency == target && rate.AsOfDate <= date)
            .OrderByDescending(rate => rate.AsOfDate)
            .Select(rate => new FxRateReading(rate.Rate, rate.AsOfDate, rate.Source))
            .FirstOrDefaultAsync(cancellationToken);

        return row ?? throw new FxRateNotFoundException(source, target, date);
    }

    public async Task<PagedList<StoredFxRate>> ListRatesAsync(
        string baseCurrency,
        string quoteCurrency,
        DateOnly? from,
        DateOnly? to,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var source = baseCurrency.Trim().ToUpperInvariant();
        var target = quoteCurrency.Trim().ToUpperInvariant();
        if (source == target)
        {
            return new PagedList<StoredFxRate> { Items = [], Page = page, PageSize = pageSize, TotalCount = 0 };
        }

        var query = db.FxRates.AsNoTracking().Where(row => row.BaseCurrency == source && row.QuoteCurrency == target);
        if (from is { } first)
        {
            query = query.Where(row => row.AsOfDate >= first);
        }

        if (to is { } last)
        {
            query = query.Where(row => row.AsOfDate <= last);
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(row => row.AsOfDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(row => new { row.AsOfDate, row.BaseCurrency, row.QuoteCurrency, row.Rate, row.Source, row.FetchedAt })
            .ToListAsync(cancellationToken);
        var items = rows
            .Select(row => new StoredFxRate(row.AsOfDate, row.BaseCurrency.Trim(), row.QuoteCurrency.Trim(), row.Rate, row.Source, row.FetchedAt))
            .ToList();
        return new PagedList<StoredFxRate> { Items = items, Page = page, PageSize = pageSize, TotalCount = total };
    }

    public async Task UpsertAsync(
        IReadOnlyList<FxRateQuote> quotes,
        DateTimeOffset fetchedAt,
        CancellationToken cancellationToken = default)
    {
        if (quotes.Count == 0)
        {
            return;
        }

        var normalized = quotes
            .Select(quote => quote with
            {
                BaseCurrency = quote.BaseCurrency.Trim().ToUpperInvariant(),
                QuoteCurrency = quote.QuoteCurrency.Trim().ToUpperInvariant()
            })
            .ToList();
        var bases = normalized.Select(quote => quote.BaseCurrency).Distinct().ToList();
        var first = normalized.Min(quote => quote.AsOfDate);
        var last = normalized.Max(quote => quote.AsOfDate);
        var existing = (await db.FxRates
                .Where(row => bases.Contains(row.BaseCurrency) && row.AsOfDate >= first && row.AsOfDate <= last)
                .ToListAsync(cancellationToken))
            .ToDictionary(row => (row.BaseCurrency.Trim(), row.QuoteCurrency.Trim(), row.AsOfDate));

        // Market data history R4–R6: a final row is never updated; a missing one may still be inserted.
        var today = DateOnly.FromDateTime(fetchedAt.UtcDateTime);
        foreach (var quote in normalized)
        {
            var key = (quote.BaseCurrency, quote.QuoteCurrency, quote.AsOfDate);
            if (existing.TryGetValue(key, out var row))
            {
                if (MarketDataRow.IsFinal(quote.AsOfDate, today))
                {
                    continue;
                }
            }
            else
            {
                row = new FxRate
                {
                    BaseCurrency = quote.BaseCurrency,
                    QuoteCurrency = quote.QuoteCurrency,
                    AsOfDate = quote.AsOfDate
                };
                db.FxRates.Add(row);
                existing.Add(key, row);
            }

            row.Rate = quote.Rate;
            row.Source = "Frankfurter";
            row.FetchedAt = fetchedAt;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> HasAsOfDateForEveryPairAsync(
        IReadOnlyList<string> currencies,
        DateOnly asOf,
        CancellationToken cancellationToken = default)
    {
        var codes = currencies
            .Select(code => code.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (codes.Count < 2)
        {
            return true;
        }

        var stored = await db.FxRates.AsNoTracking()
            .Where(row => row.AsOfDate == asOf)
            .Select(row => new { row.BaseCurrency, row.QuoteCurrency })
            .ToListAsync(cancellationToken);
        var pairs = stored
            .Select(row => $"{row.BaseCurrency.Trim()}|{row.QuoteCurrency.Trim()}")
            .ToHashSet(StringComparer.Ordinal);

        foreach (var baseCurrency in codes)
        {
            foreach (var quoteCurrency in codes)
            {
                if (baseCurrency == quoteCurrency)
                {
                    continue;
                }

                if (!pairs.Contains($"{baseCurrency}|{quoteCurrency}"))
                {
                    return false;
                }
            }
        }

        return true;
    }
}
