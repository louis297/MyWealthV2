using Hangfire;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Reports;

namespace MyWealthV2.Infrastructure.Reports;

public sealed class HangfireReportScheduler(IBackgroundJobClient jobs) : IReportScheduler
{
    public void Enqueue(Guid executionId)
    {
        jobs.Enqueue<ReportJob>("reports", job => job.Execute(executionId));
    }

    public void AddOrUpdateRecurring(Guid scheduleId, string cron)
    {
    }

    public void RemoveRecurring(Guid scheduleId)
    {
    }
}
