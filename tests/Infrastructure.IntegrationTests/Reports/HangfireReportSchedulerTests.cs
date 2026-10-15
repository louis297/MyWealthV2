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

    private static IHost BuildHost()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddHangfire(configuration => configuration.UseInMemoryStorage());
        builder.Services.AddSingleton<IReportScheduler, HangfireReportScheduler>();
        return builder.Build();
    }
}
