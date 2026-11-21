using System.Collections.Concurrent;
using System.Globalization;
using MyWealthV2.Application.BankFeed;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.BankFeed;

/// <summary>
/// The functional host's bank feed (deposit-matching §10; ADR 0021 rule 19). Tests add lines to an account's feed;
/// a cursor is the number of places already passed. <see cref="PageSize"/> caps a page below the platform's limit.
/// Each page carries the bank's balance at its cursor (bank-reconciliation R2): the account's opening balance plus
/// every line with a place up to the cursor, each id counted once, lines left out of the feed included.
/// </summary>
internal sealed class InMemoryBankFeed : IBankFeed
{
    private readonly ConcurrentDictionary<string, List<Place>> _feeds = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, BankFeedFailure> _failures = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, decimal> _openings = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, bool> _noBalance = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<BankFeedRequest> _requests = new();

    public int? PageSize { get; set; }

    public IReadOnlyList<BankFeedRequest> Requests => _requests.ToList();

    public void Reset()
    {
        _feeds.Clear();
        _failures.Clear();
        _openings.Clear();
        _noBalance.Clear();
        _requests.Clear();
        PageSize = null;
    }

    public BankFeedLine Add(string accountNumber, BankFeedLine line) => Store(accountNumber, line, omitted: false);

    /// <summary>A line in the bank's books that the feed never returns; it counts in the balance from its place
    /// (bank-reconciliation R28).</summary>
    public BankFeedLine Omit(string accountNumber, BankFeedLine line) => Store(accountNumber, line, omitted: true);

    /// <summary>The money in the account before its first line (bank-reconciliation R4).</summary>
    public void SetOpeningBalance(string accountNumber, decimal opening) => _openings[accountNumber] = opening;

    /// <summary>Pages for <paramref name="accountNumber"/> carry no balance until cleared (bank-reconciliation R5).</summary>
    public void SendNoBalance(string accountNumber, bool noBalance) => _noBalance[accountNumber] = noBalance;

    private BankFeedLine Store(string accountNumber, BankFeedLine line, bool omitted)
    {
        var feed = _feeds.GetOrAdd(accountNumber, _ => []);
        lock (feed)
        {
            feed.Add(new Place(line, omitted));
        }

        return line;
    }

    /// <summary>A credit line with a fresh id, today's value date and the given fields.</summary>
    public BankFeedLine AddReceipt(
        string accountNumber,
        decimal amount,
        string currency,
        string? reference,
        string? payerName = "J SMITH",
        string? payerAccount = "12-3456-7890042-00",
        string? id = null,
        BankLineDirection direction = BankLineDirection.Credit,
        DateOnly? valueDate = null) =>
        Add(accountNumber, new BankFeedLine(
            id ?? "fake-" + Guid.NewGuid().ToString("N"),
            valueDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
            direction,
            amount,
            currency,
            payerName,
            payerAccount,
            reference));

    /// <summary>Every call for <paramref name="accountNumber"/> fails with <paramref name="failure"/> until cleared.</summary>
    public void Fail(string accountNumber, BankFeedFailure? failure)
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

    public Task<BankFeedResult> GetStatementLinesAsync(BankFeedRequest request, CancellationToken cancellationToken)
    {
        _requests.Enqueue(request);
        if (_failures.TryGetValue(request.AccountNumber, out var failure))
        {
            return Task.FromResult(BankFeedResult.Failed(failure));
        }

        // The bank knows every account; one without lines has an empty statement.
        var feed = _feeds.GetOrAdd(request.AccountNumber, _ => []);
        List<Place> places;
        lock (feed)
        {
            places = feed.ToList();
        }

        var after = request.Cursor is null ? 0 : int.Parse(request.Cursor, CultureInfo.InvariantCulture);
        var take = Math.Min(request.Limit, PageSize ?? int.MaxValue);
        var page = new List<BankFeedLine>();
        var next = after;
        while (next < places.Count && page.Count < take)
        {
            if (!places[next].Omitted)
            {
                page.Add(places[next].Line);
            }

            next++;
        }

        // A page that ends on its last delivered line leaves the omitted lines after it to the next page, unless no
        // delivered line follows them.
        if (places.Skip(next).All(place => place.Omitted))
        {
            next = places.Count;
        }

        var cursor = next.ToString(CultureInfo.InvariantCulture);
        return Task.FromResult(BankFeedResult.Succeeded(
            new BankFeedPage(page, cursor, next < places.Count, BalanceAt(request.AccountNumber, places, next))));
    }

    private decimal? BalanceAt(string accountNumber, List<Place> places, int cursor)
    {
        if (_noBalance.TryGetValue(accountNumber, out var none) && none)
        {
            return null;
        }

        var opening = _openings.TryGetValue(accountNumber, out var value) ? value : 0m;
        return opening + places.Take(cursor)
            .DistinctBy(place => place.Line.Id, StringComparer.OrdinalIgnoreCase)
            .Sum(place => place.Line.Direction == BankLineDirection.Debit ? -(place.Line.Amount ?? 0m) : place.Line.Amount ?? 0m);
    }

    private sealed record Place(BankFeedLine Line, bool Omitted);
}
