using Hangfire;
using MyWealthV2.Application.MarketData;

namespace MyWealthV2.Infrastructure.MarketData;

public static class InstrumentPriceRecurringJob
{
    public const string JobId = "instrument-price-sync";

    public static void Register(IRecurringJobManager recurring)
    {
        recurring.AddOrUpdate<InstrumentPriceSyncJob>(
            JobId,
            "prices",
            job => job.Sync(CancellationToken.None),
            "0 * * * *",
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
    }
}
