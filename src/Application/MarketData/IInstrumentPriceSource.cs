namespace MyWealthV2.Application.MarketData;

public interface IInstrumentPriceSource
{
    /// <summary>
    /// The bars of one symbol (docs/features/market-data-history.md R7, R8): with <paramref name="from"/>, every bar
    /// the source returns from that date; without it, the latest bar only. Null when the source rejects the symbol.
    /// </summary>
    Task<SourcedInstrumentBars?> GetBarsAsync(
        string symbol,
        string quoteCurrency,
        DateOnly? from = null,
        CancellationToken cancellationToken = default);
}

/// <summary>The bars a source returned for one symbol, in <see cref="Currency"/>.</summary>
public sealed record SourcedInstrumentBars(string Currency, IReadOnlyList<SourcedPriceBar> Bars);

/// <summary>One end-of-day bar: its date and its unadjusted close, or null when the bar has no readable close.</summary>
public readonly record struct SourcedPriceBar(DateOnly AsOfDate, decimal? Close);
