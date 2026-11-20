using System.Collections.Concurrent;
using System.Globalization;
using MyWealthV2.Application.BankFeed;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.BankFeed;

/// <summary>
/// The functional host's bank feed (deposit-matching §10; ADR 0021 rule 19). Tests add lines to an account's feed;
/// a cursor is the number of lines already returned. <see cref="PageSize"/> caps a page below the platform's limit.
/// </summary>
internal sealed class InMemoryBankFeed : IBankFeed
{
    private readonly ConcurrentDictionary<string, List<BankFeedLine>> _feeds = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, BankFeedFailure> _failures = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<BankFeedRequest> _requests = new();

    public int? PageSize { get; set; }

    public IReadOnlyList<BankFeedRequest> Requests => _requests.ToList();

    public void Reset()
    {
        _feeds.Clear();
        _failures.Clear();
        _requests.Clear();
        PageSize = null;
    }

    public BankFeedLine Add(string accountNumber, BankFeedLine line)
    {
        var feed = _feeds.GetOrAdd(accountNumber, _ => []);
        lock (feed)
        {
            feed.Add(line);
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
        List<BankFeedLine> lines;
        lock (feed)
        {
            lines = feed.ToList();
        }

        var after = request.Cursor is null ? 0 : int.Parse(request.Cursor, CultureInfo.InvariantCulture);
        var take = Math.Min(request.Limit, PageSize ?? int.MaxValue);
        var page = lines.Skip(after).Take(take).ToList();
        var cursor = (after + page.Count).ToString(CultureInfo.InvariantCulture);
        return Task.FromResult(BankFeedResult.Succeeded(
            new BankFeedPage(page, cursor, after + page.Count < lines.Count)));
    }
}
