using System.Collections.Concurrent;
using System.Globalization;
using MyWealthV2.Application.CorporateActions;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.CorporateActions;

/// <summary>
/// The functional host's corporate action feed (corporate-actions §10; ADR 0021 rule 19). Tests add items to a
/// connection's feed; a cursor is the number of places already passed. <see cref="PageSize"/> caps a page below the
/// platform's limit, and <see cref="Cursor"/> overrides the cursor of the next answers.
/// </summary>
internal sealed class InMemoryCorporateActionFeed : ICorporateActionFeed
{
    private readonly ConcurrentDictionary<string, List<CorporateActionFeedItem>> _feeds = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, MarketDataFailure> _failures = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<(string Connection, string? Cursor, int Limit)> _requests = new();

    public int? PageSize { get; set; }

    public string? Cursor { get; set; }

    public IReadOnlyList<(string Connection, string? Cursor, int Limit)> Requests => _requests.ToList();

    public void Reset()
    {
        _feeds.Clear();
        _failures.Clear();
        _requests.Clear();
        PageSize = null;
        Cursor = null;
    }

    public CorporateActionFeedItem Add(string connection, CorporateActionFeedItem item)
    {
        var feed = _feeds.GetOrAdd(connection, _ => []);
        lock (feed)
        {
            feed.Add(item);
        }

        return item;
    }

    /// <summary>Every call for <paramref name="connection"/> fails with <paramref name="failure"/> until cleared.</summary>
    public void Fail(string connection, MarketDataFailure? failure)
    {
        if (failure is null)
        {
            _failures.TryRemove(connection, out _);
        }
        else
        {
            _failures[connection] = failure.Value;
        }
    }

    public Task<CorporateActionFeedResult> GetAnnouncementsAsync(
        string connection,
        string? cursor,
        int limit,
        CancellationToken cancellationToken)
    {
        _requests.Enqueue((connection, cursor, limit));
        if (_failures.TryGetValue(connection, out var failure))
        {
            return Task.FromResult(CorporateActionFeedResult.Failed(failure));
        }

        var after = cursor is null ? 0 : int.Parse(cursor, CultureInfo.InvariantCulture);
        var feed = _feeds.GetOrAdd(connection, _ => []);
        List<CorporateActionFeedItem> items;
        int total;
        lock (feed)
        {
            total = feed.Count;
            items = feed.Skip(after).Take(Math.Min(limit, PageSize ?? limit)).ToList();
        }

        var next = after + items.Count;
        return Task.FromResult(CorporateActionFeedResult.Succeeded(new CorporateActionFeedPage(
            items, Cursor ?? next.ToString(CultureInfo.InvariantCulture), next < total)));
    }
}
