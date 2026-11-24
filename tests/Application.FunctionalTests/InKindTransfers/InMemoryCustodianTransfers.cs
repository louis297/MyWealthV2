using System.Collections.Concurrent;
using MyWealthV2.Application.Custodian;

namespace MyWealthV2.Application.FunctionalTests.InKindTransfers;

/// <summary>
/// The functional host's custodian transfers (in-kind-transfers §9.1, §10; ADR 0021 rule 19). Every call is recorded.
/// A test sets the next answers; with none queued, the custodian accepts with a transfer id of its own.
/// </summary>
internal sealed class InMemoryCustodianTransfers : ICustodianTransfers
{
    private readonly ConcurrentQueue<CustodianTransferRequest> _requests = new();
    private readonly ConcurrentQueue<CustodianTransferResult> _answers = new();
    private int _next;

    public IReadOnlyList<CustodianTransferRequest> Requests => _requests.ToList();

    public void Reset()
    {
        _requests.Clear();
        _answers.Clear();
        _next = 0;
    }

    /// <summary>The answer to the next call, then the default again.</summary>
    public void Answer(CustodianTransferResult result) => _answers.Enqueue(result);

    public Task<CustodianTransferResult> SendAsync(CustodianTransferRequest request, CancellationToken cancellationToken)
    {
        _requests.Enqueue(request);
        return Task.FromResult(_answers.TryDequeue(out var answer)
            ? answer
            : CustodianTransferResult.Accepted($"fake-T{Interlocked.Increment(ref _next)}"));
    }
}
