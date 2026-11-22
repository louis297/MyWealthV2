using System.Collections.Concurrent;
using System.Globalization;
using MyWealthV2.Application.Broker;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.Broker;

/// <summary>
/// The functional host's broker feed (orders §10; ADR 0021 rule 19). Tests add reports to a connection's feed; a
/// cursor is the number of places already passed. <see cref="PageSize"/> caps a page below the platform's limit, and
/// <see cref="Cursor"/> overrides the cursor of the next answers.
/// </summary>
internal sealed class InMemoryBrokerFeed : IBrokerFeed
{
    private readonly ConcurrentDictionary<string, List<BrokerFeedItem>> _feeds = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, BrokerFailure> _failures = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<(string Connection, string? Cursor, int Limit)> _requests = new();
    private int _next;

    public int? PageSize { get; set; }

    public string? Cursor { get; set; }

    public IReadOnlyList<(string Connection, string? Cursor, int Limit)> Requests => _requests.ToList();

    public void Reset()
    {
        _feeds.Clear();
        _failures.Clear();
        _requests.Clear();
        _next = 0;
        PageSize = null;
        Cursor = null;
    }

    /// <summary>An outside id no report of this feed has yet.</summary>
    public string NextId() => $"fake-X{Interlocked.Increment(ref _next)}";

    public BrokerFeedItem Add(BrokerFeedItem item, string connection = FakeBrokerConnections.Simulator)
    {
        var feed = _feeds.GetOrAdd(connection, _ => []);
        lock (feed)
        {
            feed.Add(item);
        }

        return item;
    }

    /// <summary>A <c>Fill</c> report of an order: trade date today, settlement two days later.</summary>
    public BrokerFeedItem AddFill(
        Guid orderId,
        decimal quantity,
        decimal price,
        string? contractNote = "CN-1",
        DateOnly? tradeDate = null,
        DateOnly? settlementDate = null,
        string connection = FakeBrokerConnections.Simulator)
    {
        var traded = tradeDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        return Add(
            new BrokerFeedItem(
                NextId(), ExecutionReportKind.Fill, orderId.ToString(), "fake-O1", quantity, price, traded,
                settlementDate ?? traded.AddDays(2), contractNote, null, null, null),
            connection);
    }

    /// <summary>A <c>Cancelled</c> or <c>Expired</c> report of an order.</summary>
    public BrokerFeedItem AddEnd(Guid orderId, ExecutionReportKind kind, string? reason = null) =>
        Add(new BrokerFeedItem(NextId(), kind, orderId.ToString(), "fake-O1", null, null, null, null, null, null, null, reason));

    /// <summary>A <c>Settled</c> report naming the fill by the outside id of its <c>Fill</c> report.</summary>
    public BrokerFeedItem AddSettled(Guid orderId, string fillOutsideId, DateOnly? settledDate = null) =>
        Add(new BrokerFeedItem(
            NextId(), ExecutionReportKind.Settled, orderId.ToString(), "fake-O1", null, null, null, null, null, fillOutsideId,
            settledDate ?? DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2), null));

    /// <summary>Every call for <paramref name="connection"/> fails with <paramref name="failure"/> until cleared.</summary>
    public void Fail(BrokerFailure? failure, string connection = FakeBrokerConnections.Simulator)
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

    public Task<BrokerFeedResult> GetReportsAsync(string connection, string? cursor, int limit, CancellationToken cancellationToken)
    {
        _requests.Enqueue((connection, cursor, limit));
        if (_failures.TryGetValue(connection, out var failure))
        {
            return Task.FromResult(BrokerFeedResult.Failed(failure));
        }

        var after = cursor is null ? 0 : int.Parse(cursor, CultureInfo.InvariantCulture);
        var feed = _feeds.GetOrAdd(connection, _ => []);
        List<BrokerFeedItem> items;
        int total;
        lock (feed)
        {
            total = feed.Count;
            items = feed.Skip(after).Take(Math.Min(limit, PageSize ?? limit)).ToList();
        }

        var next = after + items.Count;
        return Task.FromResult(BrokerFeedResult.Succeeded(new BrokerFeedPage(
            items, Cursor ?? next.ToString(CultureInfo.InvariantCulture), next < total)));
    }
}
