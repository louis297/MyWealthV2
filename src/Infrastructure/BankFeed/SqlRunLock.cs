namespace MyWealthV2.Infrastructure.BankFeed;

/// <summary>
/// A named lock in SQL Server (<c>sp_getapplock</c>, session owner, no wait), held on its own connection while a
/// run goes on, so a second run of the same job on any worker ends at once (deposit-matching R6).
/// </summary>
public sealed class SqlRunLock(string connectionString)
{
    private readonly string _connectionString = connectionString;

    /// <summary>The held lock, or null when another run holds it. Dispose to release.</summary>
    public Task<IAsyncDisposable?> TryAcquireAsync(string name, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
