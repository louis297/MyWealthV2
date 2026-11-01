using Hangfire;

namespace MyWealthV2.Infrastructure.Data;

public static class OutboxRecurringJob
{
    public const string JobId = "outbox";

    public static void Register(IRecurringJobManager recurring)
    {
        recurring.AddOrUpdate<OutboxProcessor>(
            JobId,
            "outbox",
            processor => processor.Process(),
            "*/10 * * * * *");
    }
}
