using Microsoft.Extensions.Logging;
using MyWealthV2.Application.Common.Interfaces;

namespace MyWealthV2.Application.Custodian;

/// <summary>What a pull of one connection did: the safekeeping accounts whose read failed (in-kind-transfers R9, R10).</summary>
public sealed record CustodianPullOutcome(IReadOnlyList<Guid> FailedSafekeepingAccounts)
{
    public bool AnyFailed => FailedSafekeepingAccounts.Count > 0;
}

/// <summary>
/// Reads the feed of every active safekeeping account of one connection through <see cref="ICustodianFeed"/> and
/// stores each new custody movement once, with the new cursor and the page's positions, page by page
/// (docs/features/in-kind-transfers.md R8–R13; ADR 0021 rules 6, 7, 15, 16).
/// </summary>
public sealed class PullCustodyMovements(
    IApplicationDbContext db,
    ICustodianFeed feed,
    ICustodianConnections connections,
    TimeProvider time,
    ILogger<PullCustodyMovements> logger)
{
    public const int PageLimit = 100;
    public const int MaxPagesPerAccount = 20;

    public Task<CustodianPullOutcome> RunAsync(string connection, CancellationToken cancellationToken = default)
    {
        _ = (db, feed, connections, time, logger);
        throw new NotImplementedException();
    }
}
