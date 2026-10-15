namespace MyWealthV2.Application.Common.Interfaces;

public interface IReportScheduler
{
    void Enqueue(Guid executionId);

    void AddOrUpdateRecurring(Guid scheduleId, string cron);

    void RemoveRecurring(Guid scheduleId);
}
