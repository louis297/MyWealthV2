using Hangfire;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Reports;

namespace MyWealthV2.Infrastructure.Reports;

public sealed class HangfireReportScheduler(IBackgroundJobClient jobs, IRecurringJobManager recurring)
    : IReportScheduler
{
    public void Enqueue(Guid executionId)
    {
        jobs.Enqueue<ReportJob>("reports", job => job.Execute(executionId));
    }

    public void AddOrUpdateRecurring(Guid scheduleId, string cron)
    {
        recurring.AddOrUpdate<ReportJob>(
            scheduleId.ToString("D"),
            "reports",
            job => job.RunScheduled(scheduleId),
            cron);
    }

    public void RemoveRecurring(Guid scheduleId)
    {
    }
}
