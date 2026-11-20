using Microsoft.Extensions.Logging;
using MyWealthV2.Application.Common.Interfaces;

namespace MyWealthV2.Application.BankFeed;

/// <summary>What a pull of one connection did: the client money accounts whose read failed (deposit-matching R7).</summary>
public sealed record BankPullOutcome(IReadOnlyList<Guid> FailedClientMoneyAccounts)
{
    public bool AnyFailed => FailedClientMoneyAccounts.Count > 0;
}

/// <summary>
/// Reads the statement of every active client money account of one connection through <see cref="IBankFeed"/> and
/// stores each new line once, with the new cursor, page by page (deposit-matching R5, R7–R12, R14; ADR 0021 rules 6,
/// 7, 15, 16). Runs as the worker's system user and reads across tenants explicitly.
/// </summary>
public sealed class PullBankStatements
{
    private readonly IApplicationDbContext _db;
    private readonly IBankFeed _feed;
    private readonly IBankConnections _connections;
    private readonly TimeProvider _time;
    private readonly ILogger<PullBankStatements> _logger;

    public PullBankStatements(
        IApplicationDbContext db,
        IBankFeed feed,
        IBankConnections connections,
        TimeProvider time,
        ILogger<PullBankStatements> logger)
    {
        _db = db;
        _feed = feed;
        _connections = connections;
        _time = time;
        _logger = logger;
    }

    public const int PageLimit = 100;
    public const int MaxPagesPerAccount = 20;

    public Task<BankPullOutcome> RunAsync(string connection, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
}
