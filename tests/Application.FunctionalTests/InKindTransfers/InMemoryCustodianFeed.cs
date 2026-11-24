using System.Collections.Concurrent;
using System.Globalization;
using MyWealthV2.Application.Custodian;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.InKindTransfers;

/// <summary>
/// The functional host's custodian feed (in-kind-transfers §10; ADR 0021 rule 19). Tests add movements to a safekeeping
/// account's feed; a cursor is the number of places already passed. Each page carries the custodian's positions at its
/// cursor: the signed sum per symbol of every movement up to the cursor, movements left out of the feed included.
/// </summary>
internal sealed class InMemoryCustodianFeed : ICustodianFeed
{
    private readonly ConcurrentDictionary<string, List<(CustodianFeedMovement Movement, bool Omitted)>> _feeds =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CustodianFailure> _failures = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IReadOnlyList<CustodianFeedPosition>?> _positions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<CustodianFeedRequest> _requests = new();

    public int? PageSize { get; set; }

    public IReadOnlyList<CustodianFeedRequest> Requests => _requests.ToList();

    public void Reset()
    {
        _feeds.Clear();
        _failures.Clear();
        _positions.Clear();
        _requests.Clear();
        PageSize = null;
    }

    public CustodianFeedMovement Add(string accountNumber, CustodianFeedMovement movement) => Store(accountNumber, movement, false);

    /// <summary>A movement in the custodian's books that the feed never returns; it counts in the positions (R12).</summary>
    public CustodianFeedMovement Omit(string accountNumber, CustodianFeedMovement movement) => Store(accountNumber, movement, true);

    /// <summary>A movement with a fresh id, today's settlement date and the given fields.</summary>
    public CustodianFeedMovement AddMovement(
        string accountNumber,
        CustodyMovementDirection direction,
        string symbol,
        decimal quantity,
        CustodyMovementKind? kind = CustodyMovementKind.Transfer,
        string currency = "USD",
        string? instructionId = null,
        string? tradeReference = null,
        string? corporateActionId = null,
        string? id = null,
        string? counterpartyName = "Harbour Brokers",
        string? counterpartyAccount = "HB-0044172") =>
        Add(accountNumber, new CustodianFeedMovement(
            id ?? "fake-M" + Guid.NewGuid().ToString("N"), direction, kind, symbol, currency, quantity,
            DateOnly.FromDateTime(DateTime.UtcNow), instructionId, tradeReference, corporateActionId, counterpartyName,
            counterpartyAccount, null));

    /// <summary>Every call for <paramref name="accountNumber"/> fails with <paramref name="failure"/> until cleared.</summary>
    public void Fail(string accountNumber, CustodianFailure? failure)
    {
        if (failure is null)
        {
            _failures.TryRemove(accountNumber, out _);
        }
        else
        {
            _failures[accountNumber] = failure.Value;
        }
    }

    /// <summary>Pages for <paramref name="accountNumber"/> carry these positions (null: none) in place of the sums.</summary>
    public void SetPositions(string accountNumber, IReadOnlyList<CustodianFeedPosition>? positions) => _positions[accountNumber] = positions;

    private CustodianFeedMovement Store(string accountNumber, CustodianFeedMovement movement, bool omitted)
    {
        var feed = _feeds.GetOrAdd(accountNumber, _ => []);
        lock (feed)
        {
            feed.Add((movement, omitted));
        }

        return movement;
    }

    public Task<CustodianFeedResult> GetMovementsAsync(CustodianFeedRequest request, CancellationToken cancellationToken)
    {
        _requests.Enqueue(request);
        if (_failures.TryGetValue(request.AccountNumber, out var failure))
        {
            return Task.FromResult(CustodianFeedResult.Failed(failure));
        }

        var feed = _feeds.GetOrAdd(request.AccountNumber, _ => []);
        List<(CustodianFeedMovement Movement, bool Omitted)> places;
        lock (feed)
        {
            places = feed.ToList();
        }

        var start = request.Cursor is null ? 0 : int.Parse(request.Cursor, CultureInfo.InvariantCulture);
        var limit = Math.Min(request.Limit, PageSize ?? request.Limit);
        var items = new List<CustodianFeedMovement>();
        var place = start;
        while (place < places.Count && items.Count < limit)
        {
            if (!places[place].Omitted)
            {
                items.Add(places[place].Movement);
            }

            place++;
        }

        // Trailing omitted movements are passed over too, so the cursor moves past them.
        while (place < places.Count && places[place].Omitted)
        {
            place++;
        }

        var positions = _positions.TryGetValue(request.AccountNumber, out var set)
            ? set
            : places.Take(place)
                .Where(entry => entry.Movement.Symbol is not null && entry.Movement.Quantity is not null)
                .GroupBy(entry => (entry.Movement.Symbol!, entry.Movement.Currency))
                .Select(group => new CustodianFeedPosition(group.Key.Item1, group.Key.Currency, group.Sum(entry =>
                    entry.Movement.Direction == CustodyMovementDirection.Out ? -entry.Movement.Quantity!.Value : entry.Movement.Quantity!.Value)))
                .Where(position => position.Quantity != 0m)
                .ToList();
        return Task.FromResult(CustodianFeedResult.Succeeded(new CustodianFeedPage(
            items, place.ToString(CultureInfo.InvariantCulture), place < places.Count, positions)));
    }
}
