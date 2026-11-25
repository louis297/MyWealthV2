using MyWealthV2.Application.Common.Models;
using MyWealthV2.Domain.ValueObjects;

namespace MyWealthV2.Application.Common.Interfaces;

public interface IMarketData
{
    Money? TryGetPrice(int instrumentId, DateTimeOffset? asOf = null);

    /// <summary>
    /// The row <see cref="TryGetPrice"/> reads for <paramref name="asOf"/> (the latest stored price dated on or before
    /// its UTC date, or today), with its date and source; null when there is none (market-data-history R17).
    /// </summary>
    InstrumentPriceReading? TryGetPriceDetail(int instrumentId, DateTimeOffset? asOf = null);

    /// <summary>
    /// The stored prices of <paramref name="instrumentId"/> dated inside the inclusive range, newest first, with the
    /// total count (market-data-history R18). Never calls a source.
    /// </summary>
    Task<PagedList<StoredInstrumentPrice>> ListPricesAsync(
        int instrumentId,
        DateOnly? from,
        DateOnly? to,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}

/// <summary>One stored price, in the instrument's quote currency, with its as-of date and source.</summary>
public sealed record InstrumentPriceReading(Money Price, DateOnly AsOfDate, string Source);

/// <summary>One row of <c>InstrumentPrices</c> as a list shows it.</summary>
public sealed record StoredInstrumentPrice(DateOnly AsOfDate, decimal Price, string Source, DateTimeOffset FetchedAt);
