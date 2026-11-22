using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.MarketData;

/// <summary>
/// The market prices port (docs/features/simulated-instruments.md §7; ADR 0021 rule 1): the current price of every
/// instrument the party of one market data connection prices, in one call. Infrastructure dispatches to the adapter of
/// the connection. A failure comes back as a result, never as an exception the caller must parse (R25).
/// </summary>
public interface IMarketPrices
{
    Task<MarketPricesResult> GetPricesAsync(string connection, CancellationToken cancellationToken);
}

/// <summary>One price as the party sent it. Fields the party left out are null; the run skips such an item (R11).
/// <paramref name="Date"/> is the party's own date, which the platform does not store (R10).</summary>
public sealed record MarketPriceItem(string? Symbol, string? Currency, decimal? Price, DateOnly? Date);

/// <summary>The items, or the reason the call failed.</summary>
public sealed class MarketPricesResult
{
    private MarketPricesResult(IReadOnlyList<MarketPriceItem>? items, MarketDataFailure? failure)
    {
        Items = items;
        Failure = failure;
    }

    public IReadOnlyList<MarketPriceItem>? Items { get; }

    public MarketDataFailure? Failure { get; }

    public static MarketPricesResult Succeeded(IReadOnlyList<MarketPriceItem> items) => new(items, null);

    public static MarketPricesResult Failed(MarketDataFailure failure) => new(null, failure);
}
