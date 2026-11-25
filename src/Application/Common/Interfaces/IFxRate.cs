using MyWealthV2.Application.Common.Models;

namespace MyWealthV2.Application.Common.Interfaces;

public interface IFxRate
{
    Task<decimal> GetRateAsync(
        string from,
        string to,
        DateTimeOffset? asOf = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The stored row for the directed pair with the latest <c>AsOfDate</c> on or before the date of
    /// <paramref name="asOf"/>, with its as-of date and source. The inverse pair is not used.
    /// Throws <see cref="Exceptions.FxRateNotFoundException"/> when there is none.
    /// </summary>
    Task<FxRateReading> GetRateDetailAsync(
        string from,
        string to,
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The stored rates of the directed pair dated inside the inclusive range, newest first, with the total count
    /// (market-data-history R18). The inverse pair is not used; a currency against itself has no rows. Never calls a
    /// source.
    /// </summary>
    Task<PagedList<StoredFxRate>> ListRatesAsync(
        string baseCurrency,
        string quoteCurrency,
        DateOnly? from,
        DateOnly? to,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}

/// <summary>One row of <c>FxRates</c> as a list shows it: quote currency per 1 base currency.</summary>
public sealed record StoredFxRate(
    DateOnly AsOfDate,
    string BaseCurrency,
    string QuoteCurrency,
    decimal Rate,
    string Source,
    DateTimeOffset FetchedAt);

/// <summary>One stored rate: quote currency per 1 base currency.</summary>
public sealed record FxRateReading(decimal Rate, DateOnly AsOfDate, string Source);
