using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.Broker;

/// <summary>What a pull of one connection did: the failure that stopped it, if any, and how many reports it stored.</summary>
public sealed record BrokerPullOutcome(BrokerFailure? Failure, int Stored)
{
    public bool Failed => Failure is not null;
}

/// <summary>
/// Reads the execution reports of one broker connection through <see cref="IBrokerFeed"/> after the feed's cursor and
/// stores each new one once, with the new cursor, page by page (docs/features/orders.md R24).
/// </summary>
public sealed class PullExecutionReports
{
    public const int PageLimit = 100;
    public const int MaxPages = 20;

    public Task<BrokerPullOutcome> RunAsync(string connection, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
}
