using Hangfire;

namespace MyWealthV2.Infrastructure.Data;

public static class OutboxRecurringJob
{
    public static void Register(IRecurringJobManager recurring)
    {
        _ = recurring;
    }
}
