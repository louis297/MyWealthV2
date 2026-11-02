using Hangfire;
using MyWealthV2.Application.Fx;

namespace MyWealthV2.Infrastructure.MarketData;

public static class FxRateRecurringJob
{
    public const string JobId = "fx-rate-sync";

    public static void Register(IRecurringJobManager recurring)
    {
        recurring.AddOrUpdate<FxRateSyncJob>(
            JobId,
            "fx",
            job => job.Sync(CancellationToken.None),
            "0 * * * *",
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
    }
}
