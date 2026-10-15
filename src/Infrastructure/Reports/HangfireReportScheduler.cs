using MyWealthV2.Application.Common.Interfaces;

namespace MyWealthV2.Infrastructure.Reports;

public sealed class HangfireReportScheduler : IReportScheduler
{
    public void Enqueue(Guid executionId)
    {
    }

    public void AddOrUpdateRecurring(Guid scheduleId, string cron)
    {
    }

    public void RemoveRecurring(Guid scheduleId)
    {
    }
}
