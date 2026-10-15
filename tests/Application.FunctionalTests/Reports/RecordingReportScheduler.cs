using MyWealthV2.Application.Common.Interfaces;

namespace MyWealthV2.Application.FunctionalTests.Reports;

public sealed class RecordingReportScheduler : IReportScheduler
{
    public List<Guid> Enqueued { get; } = [];

    public List<(Guid ScheduleId, string Cron)> Recurring { get; } = [];

    public List<Guid> Removed { get; } = [];

    public void Enqueue(Guid executionId) => Enqueued.Add(executionId);

    public void AddOrUpdateRecurring(Guid scheduleId, string cron) => Recurring.Add((scheduleId, cron));

    public void RemoveRecurring(Guid scheduleId) => Removed.Add(scheduleId);

    public void Clear()
    {
        Enqueued.Clear();
        Recurring.Clear();
        Removed.Clear();
    }
}
