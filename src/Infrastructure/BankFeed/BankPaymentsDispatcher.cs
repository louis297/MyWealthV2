using Microsoft.Extensions.Logging;
using MyWealthV2.Application.BankFeed;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Infrastructure.BankFeed;

/// <summary>One adapter of the bank payments port, chosen by a connection's <c>Adapter</c> (withdrawals R58).</summary>
public interface IBankPaymentsAdapter
{
    string Adapter { get; }

    Task<BankPaymentResult> SendAsync(
        BankConnectionSettings connection,
        BankPaymentRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// <see cref="IBankPayments"/> for the hosts: sends each payment to the adapter its connection names (ADR 0021 rule 3).
/// A connection that is not configured on this host, or names no registered adapter, is unreachable.
/// </summary>
public sealed class BankPaymentsDispatcher(
    ConfiguredBankConnections connections,
    IEnumerable<IBankPaymentsAdapter> adapters,
    ILogger<BankPaymentsDispatcher> logger) : IBankPayments
{
    public Task<BankPaymentResult> SendAsync(BankPaymentRequest request, CancellationToken cancellationToken)
    {
        var settings = connections.Settings.SingleOrDefault(entry => entry.Name == request.Connection);
        var adapter = settings is null ? null : adapters.SingleOrDefault(entry => entry.Adapter == settings.Adapter);
        if (settings is null || adapter is null)
        {
            logger.LogWarning("Bank connection {Connection} has no payments adapter on this host.", request.Connection);
            return Task.FromResult(BankPaymentResult.Failed(BankFeedFailure.Unreachable));
        }

        return adapter.SendAsync(settings, request, cancellationToken);
    }
}
