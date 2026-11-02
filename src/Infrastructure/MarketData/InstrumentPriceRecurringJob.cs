using Hangfire;

namespace MyWealthV2.Infrastructure.MarketData;

public static class InstrumentPriceRecurringJob
{
    public const string JobId = "instrument-price-sync";

    public static void Register(IRecurringJobManager recurring)
    {
        _ = recurring;
    }
}
