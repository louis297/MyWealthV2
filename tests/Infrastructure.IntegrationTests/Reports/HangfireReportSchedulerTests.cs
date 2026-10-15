using System.Text.Json;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Infrastructure.Reports;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.Reports;

public class HangfireReportSchedulerTests
{
    [Test]
    public void Enqueue_PlacesReportJobExecuteOnReportsQueue()
    {
        using var host = BuildHost();
        var executionId = Guid.Parse("8c4e1a20-7b3d-4f6a-9e15-2d0c6b8a4f11");

        host.Services.GetRequiredService<IReportScheduler>().Enqueue(executionId);

        var jobs = host.Services.GetRequiredService<JobStorage>()
            .GetMonitoringApi()
            .EnqueuedJobs("reports", 0, 20);

        jobs.Count.ShouldBe(1);
        var job = jobs.Single().Value.Job;
        job.Type.Name.ShouldBe("ReportJob");
        job.Method.Name.ShouldBe("Execute");
        job.Args.Single().ShouldBe(executionId);
    }

    [Test]
    public void AddOrUpdateRecurring_UsesSchedulePublicIdAndReplacesCron()
    {
        using var host = BuildHost();
        var scheduler = host.Services.GetRequiredService<IReportScheduler>();
        var scheduleId = Guid.Parse("5a9d3c70-2e84-4b1f-8c36-7f1e0a9d5b22");

        scheduler.AddOrUpdateRecurring(scheduleId, "0 9 1 * *");

        var created = ReadRecurring(host, scheduleId);
        created.Cron.ShouldBe("0 9 1 * *");
        created.Queue.ShouldBe("reports");
        created.Method.ShouldBe("RunScheduled");
        created.Arguments.ShouldContain(scheduleId.ToString("D"));

        scheduler.AddOrUpdateRecurring(scheduleId, "15 8 * * 1");

        var updated = ReadRecurring(host, scheduleId);
        updated.Cron.ShouldBe("15 8 * * 1");
        updated.Queue.ShouldBe("reports");
        updated.Method.ShouldBe("RunScheduled");
        RecurringIds(host).Count.ShouldBe(1);
    }

    [Test]
    public void RemoveRecurring_RemovesTheScheduleJob()
    {
        using var host = BuildHost();
        var scheduler = host.Services.GetRequiredService<IReportScheduler>();
        var scheduleId = Guid.Parse("5a9d3c70-2e84-4b1f-8c36-7f1e0a9d5b22");

        scheduler.AddOrUpdateRecurring(scheduleId, "0 9 1 * *");
        scheduler.RemoveRecurring(scheduleId);

        RecurringIds(host).ShouldBeEmpty();
    }

    private static HashSet<string> RecurringIds(IHost host)
    {
        using var connection = host.Services.GetRequiredService<JobStorage>().GetConnection();
        return connection.GetAllItemsFromSet("recurring-jobs");
    }

    private static RecurringJob ReadRecurring(IHost host, Guid scheduleId)
    {
        var id = scheduleId.ToString("D");
        var ids = RecurringIds(host);
        ids.Count.ShouldBe(1);
        ids.ShouldContain(id);

        using var connection = host.Services.GetRequiredService<JobStorage>().GetConnection();
        var hash = connection.GetAllEntriesFromHash($"recurring-job:{id}");
        using var payload = JsonDocument.Parse(hash["Job"]);
        return new RecurringJob(
            hash["Cron"],
            payload.RootElement.GetProperty("Queue").GetString()!,
            payload.RootElement.GetProperty("Method").GetString()!,
            payload.RootElement.GetProperty("Arguments").GetString()!);
    }

    private sealed record RecurringJob(string Cron, string Queue, string Method, string Arguments);

    private static IHost BuildHost()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddHangfire(configuration => configuration.UseInMemoryStorage());
        builder.Services.AddSingleton<IReportScheduler, HangfireReportScheduler>();
        return builder.Build();
    }
}
