using Microsoft.EntityFrameworkCore;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Fx;
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

    public async Task UpsertAsync(
        IReadOnlyList<FxRateQuote> quotes,
        DateTimeOffset fetchedAt,
        CancellationToken cancellationToken = default)
    {
        var existing = await db.FxRates.ToListAsync(cancellationToken);
        foreach (var quote in quotes)
        {
            var baseCurrency = quote.BaseCurrency.Trim().ToUpperInvariant();
            var quoteCurrency = quote.QuoteCurrency.Trim().ToUpperInvariant();
            var row = existing.SingleOrDefault(candidate =>
                candidate.BaseCurrency.Trim() == baseCurrency
                && candidate.QuoteCurrency.Trim() == quoteCurrency
                && candidate.AsOfDate == quote.AsOfDate);
            if (row is null)
            {
                row = new FxRate
                {
                    BaseCurrency = baseCurrency,
                    QuoteCurrency = quoteCurrency,
                    AsOfDate = quote.AsOfDate
                };
                db.FxRates.Add(row);
                existing.Add(row);
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
