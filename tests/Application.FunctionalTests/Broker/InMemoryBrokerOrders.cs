using System.Collections.Concurrent;
using MyWealthV2.Application.Broker;

namespace MyWealthV2.Application.FunctionalTests.Broker;

/// <summary>
/// The functional host's broker orders (orders §10; ADR 0021 rule 19). Every call is recorded. A test sets the next
/// answers; with none queued, the broker accepts a new order with an order id of its own and accepts a cancel.
/// </summary>
internal sealed class InMemoryBrokerOrders : IBrokerOrders
{
    private readonly ConcurrentQueue<(string Connection, BrokerOrderRequest Order)> _placed = new();
    private readonly ConcurrentQueue<(string Connection, string ClientOrderId)> _cancels = new();
    private readonly ConcurrentQueue<BrokerPlaceResult> _placeAnswers = new();
    private readonly ConcurrentQueue<BrokerCancelResult> _cancelAnswers = new();
    private int _next;

    public IReadOnlyList<(string Connection, BrokerOrderRequest Order)> Placed => _placed.ToList();

    public IReadOnlyList<(string Connection, string ClientOrderId)> Cancels => _cancels.ToList();

    /// <summary>Called while a place call is out, before its answer: a test changes the order as a report would.</summary>
    public Func<BrokerOrderRequest, Task>? WhilePlacing { get; set; }

    public void Reset()
    {
        _placed.Clear();
        _cancels.Clear();
        _placeAnswers.Clear();
        _cancelAnswers.Clear();
        _next = 0;
        WhilePlacing = null;
    }

    /// <summary>The answer to the next place call, then the default again.</summary>
    public void Answer(BrokerPlaceResult result) => _placeAnswers.Enqueue(result);

    /// <summary>The answer to the next cancel call, then the default again.</summary>
    public void Answer(BrokerCancelResult result) => _cancelAnswers.Enqueue(result);

    public async Task<BrokerPlaceResult> PlaceAsync(string connection, BrokerOrderRequest order, CancellationToken cancellationToken)
    {
        _placed.Enqueue((connection, order));
        if (WhilePlacing is { } hook)
        {
            await hook(order);
        }

        return _placeAnswers.TryDequeue(out var answer)
            ? answer
            : BrokerPlaceResult.Accepted($"fake-O{Interlocked.Increment(ref _next)}");
    }

    public Task<BrokerCancelResult> CancelAsync(string connection, string clientOrderId, CancellationToken cancellationToken)
    {
        _cancels.Enqueue((connection, clientOrderId));
        return Task.FromResult(_cancelAnswers.TryDequeue(out var answer)
            ? answer
            : BrokerCancelResult.Answered(BrokerCancelAnswer.Accepted));
    }
}
