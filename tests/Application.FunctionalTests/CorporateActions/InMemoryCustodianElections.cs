using System.Collections.Concurrent;
using MyWealthV2.Application.Custodian;

namespace MyWealthV2.Application.FunctionalTests.CorporateActions;

/// <summary>
/// The functional host's custodian elections (dividends §10; ADR 0021 rule 19). Every call is recorded. A test sets
/// the next answers; with none queued, the custodian accepts.
/// </summary>
internal sealed class InMemoryCustodianElections : ICustodianElections
{
    private readonly ConcurrentQueue<CustodianElectionRequest> _requests = new();
    private readonly ConcurrentQueue<CustodianElectionResult> _answers = new();

    public IReadOnlyList<CustodianElectionRequest> Requests => _requests.ToList();

    public void Reset()
    {
        _requests.Clear();
        _answers.Clear();
    }

    /// <summary>The answer to the next call, then the default again.</summary>
    public void Answer(CustodianElectionResult result) => _answers.Enqueue(result);

    public Task<CustodianElectionResult> SubmitAsync(CustodianElectionRequest request, CancellationToken cancellationToken)
    {
        _requests.Enqueue(request);
        return Task.FromResult(_answers.TryDequeue(out var answer) ? answer : CustodianElectionResult.Accepted());
    }
}
