using Hangfire;
using MyWealthV2.Application.Fx;

namespace MyWealthV2.Infrastructure.MarketData;

public static class FxRateRecurringJob
{
    public const string JobId = "fx-rate-sync";

    public static void Register(IRecurringJobManager recurring) =>
        throw new NotImplementedException();
}
