using Microsoft.Extensions.Logging;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Application.BankFeed;

namespace MyWealthV2.Infrastructure.BankFeed;

/// <summary>One adapter of the bank feed port, chosen by a connection's <c>Adapter</c> (deposit-matching R1, R49).</summary>
public interface IBankFeedAdapter
{
    string Adapter { get; }

    Task<BankFeedResult> GetStatementLinesAsync(
        BankConnectionSettings connection,
        BankFeedRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// <see cref="IBankFeed"/> for the hosts: sends each request to the adapter its connection names (ADR 0021 rule 3).
/// A connection that is not configured on this host, or names no registered adapter, is unreachable.
/// </summary>
public sealed class BankFeedDispatcher(
    ConfiguredBankConnections connections,
    IEnumerable<IBankFeedAdapter> adapters,
    ILogger<BankFeedDispatcher> logger) : IBankFeed
{
    public Task<BankFeedResult> GetStatementLinesAsync(BankFeedRequest request, CancellationToken cancellationToken)
    {
        var settings = connections.Settings.SingleOrDefault(entry => entry.Name == request.Connection);
        var adapter = settings is null ? null : adapters.SingleOrDefault(entry => entry.Adapter == settings.Adapter);
        if (settings is null || adapter is null)
        {
            logger.LogWarning("Bank connection {Connection} has no adapter on this host.", request.Connection);
            return Task.FromResult(BankFeedResult.Failed(BankFeedFailure.Unreachable));
        }

        return adapter.GetStatementLinesAsync(settings, request, cancellationToken);
    }
}
