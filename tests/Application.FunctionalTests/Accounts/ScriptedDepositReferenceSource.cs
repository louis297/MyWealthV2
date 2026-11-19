using System.Collections.Concurrent;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.ValueObjects;
using MyWealthV2.Infrastructure.Accounts;

namespace MyWealthV2.Application.FunctionalTests.Accounts;

/// <summary>
/// The test host's <see cref="IDepositReferenceSource"/>: returns the scripted references first, then random ones
/// like the real source, and counts its draws (client-money-accounts R17). A test that scripts it calls
/// <see cref="Reset"/> in its arrange step.
/// </summary>
public sealed class ScriptedDepositReferenceSource : IDepositReferenceSource
{
    private readonly ConcurrentQueue<DepositReference> _scripted = new();
    private int _calls;

    public int Calls => Volatile.Read(ref _calls);

    public void Reset()
    {
        _scripted.Clear();
        Interlocked.Exchange(ref _calls, 0);
    }

    public void Enqueue(params DepositReference[] references)
    {
        foreach (var reference in references)
        {
            _scripted.Enqueue(reference);
        }
    }

    public DepositReference Next()
    {
        Interlocked.Increment(ref _calls);
        return _scripted.TryDequeue(out var reference)
            ? reference
            : DepositReference.Generate(CryptoRandomSymbols.Instance);
    }
}
