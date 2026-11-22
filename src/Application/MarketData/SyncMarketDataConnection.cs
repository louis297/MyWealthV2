using MyWealthV2.Application.CorporateActions;

namespace MyWealthV2.Application.MarketData;

/// <summary>What one run of a market data connection did (docs/features/corporate-actions.md R8). <see cref="Prices"/>
/// is null when the price read was skipped because the announcement pull failed.</summary>
public sealed record MarketDataSyncOutcome(
    CorporateActionPullOutcome Pull,
    AnnouncementProcessingOutcome Processing,
    ApplyCorporateActionsOutcome Apply,
    MarketPricesOutcome? Prices)
{
    /// <summary>R8, R25: the run is reported as failed when the pull failed, an item was left for the next run, or
    /// the price read failed.</summary>
    public bool Failed => Pull.Failed || Processing.Failed > 0 || Apply.HasFailures || Prices?.Failed == true;
}

/// <summary>
/// One run of a market data connection (corporate-actions R8, simulated-instruments R8): pull the corporate action
/// announcements, process the stored ones, apply the corporate actions of the connection's instruments, then read
/// prices. When the pull fails, the stored announcements are still processed and applied, and the price read is
/// skipped: a price on the new basis must not arrive before its corporate action.
/// </summary>
public sealed class SyncMarketDataConnection
{
    public Task<MarketDataSyncOutcome> RunAsync(string connection, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
}
