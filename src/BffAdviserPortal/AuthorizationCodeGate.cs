using System.Collections.Concurrent;

namespace MyWealthV2.BffAdviserPortal;

public sealed class AuthorizationCodeGate
{
    public const string OwnerItem = "bff.authorization-code";

    private readonly ConcurrentDictionary<string, Flight> _flights = new(StringComparer.Ordinal);

    public async Task<Lease> EnterAsync(string code, CancellationToken cancellationToken)
    {
        var flight = _flights.GetOrAdd(code, static _ => new Flight());
        if (await flight.Gate.WaitAsync(TimeSpan.Zero, cancellationToken))
        {
            return new Lease(true, null);
        }

        var snapshot = await flight.Result.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        return new Lease(false, snapshot);
    }

    public void Complete(string code, Snapshot? snapshot)
    {
        if (!_flights.TryGetValue(code, out var flight))
        {
            return;
        }

        flight.Result.TrySetResult(snapshot);
        if (Interlocked.Exchange(ref flight.Released, 1) == 0)
        {
            flight.Gate.Release();
            _flights.TryRemove(code, out _);
        }
    }

    public readonly record struct Lease(bool IsOwner, Snapshot? Snapshot);

    public readonly record struct Snapshot(string? AccessToken, string? IdToken, string? RefreshToken);

    private sealed class Flight
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);

        public TaskCompletionSource<Snapshot?> Result { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Released;
    }
}
