using System.Collections.Concurrent;
using MyWealthV2.Application.MarketData;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.MarketData;

/// <summary>
/// The functional host's market prices (simulated-instruments §10; ADR 0021 rule 19). Tests set the items the party
/// answers, or a failure; every call is counted.
/// </summary>
internal sealed class InMemoryMarketPrices : IMarketPrices
{
    private readonly ConcurrentQueue<string> _requests = new();
    private IReadOnlyList<MarketPriceItem> _items = [];
    private MarketDataFailure? _failure;

    public IReadOnlyList<string> Requests => _requests.ToList();

    public void Reset()
    {
        _requests.Clear();
        _items = [];
        _failure = null;
    }

    /// <summary>The next calls answer these items.</summary>
    public void Answer(params MarketPriceItem[] items)
    {
        _items = items;
        _failure = null;
    }

    /// <summary>The next calls fail with <paramref name="failure"/>.</summary>
    public void Fail(MarketDataFailure failure) => _failure = failure;

    public Task<MarketPricesResult> GetPricesAsync(string connection, CancellationToken cancellationToken)
    {
        _requests.Enqueue(connection);
        return Task.FromResult(_failure is { } failure
            ? MarketPricesResult.Failed(failure)
            : MarketPricesResult.Succeeded(_items));
    }
}
