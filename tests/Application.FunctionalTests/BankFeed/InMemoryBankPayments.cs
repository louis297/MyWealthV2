using System.Collections.Concurrent;
using MyWealthV2.Application.BankFeed;

namespace MyWealthV2.Application.FunctionalTests.BankFeed;

/// <summary>
/// The functional host's bank payments (withdrawals §9.1, §10; ADR 0021 rule 19). Every call is recorded. A test
/// sets the next answers; with none queued, the bank accepts with a payment id of its own.
/// </summary>
internal sealed class InMemoryBankPayments : IBankPayments
{
    private readonly ConcurrentQueue<BankPaymentRequest> _requests = new();
    private readonly ConcurrentQueue<BankPaymentResult> _answers = new();
    private int _next;

    public IReadOnlyList<BankPaymentRequest> Requests => _requests.ToList();

    public void Reset()
    {
        _requests.Clear();
        _answers.Clear();
        _next = 0;
    }

    /// <summary>The answer to the next call, then the default again.</summary>
    public void Answer(BankPaymentResult result) => _answers.Enqueue(result);

    public Task<BankPaymentResult> SendAsync(BankPaymentRequest request, CancellationToken cancellationToken)
    {
        _requests.Enqueue(request);
        return Task.FromResult(_answers.TryDequeue(out var answer)
            ? answer
            : BankPaymentResult.Accepted($"fake-P{Interlocked.Increment(ref _next)}"));
    }
}
