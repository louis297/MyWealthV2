using Hangfire;
using MyWealthV2.Application.Fx;

namespace MyWealthV2.Infrastructure.MarketData;

/// <summary>The Hangfire entry point of <c>fx-rate-history-sync</c>: no automatic retries, the next day's run is the
/// retry (docs/features/market-data-history.md R12).</summary>
public sealed class FxRateHistoryHangfireJob(FxRateHistorySyncJob job)
{
    [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public Task Run(CancellationToken cancellationToken) => job.Sync(cancellationToken);
}

public static class FxRateHistoryRecurringJob
{
    public const string JobId = "fx-rate-history-sync";

    public static void Register(IRecurringJobManager recurring)
    {
    }
}
