using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Infrastructure.Reports;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.Reports;

public class HangfireRebalanceSchedulerTests
{
    [Test]
    public void Enqueue_PlacesRebalanceJobOnTheRebalanceQueue()
    {
        using var host = BuildHost();
        var accountId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var handle = Guid.Parse("22222222-2222-2222-2222-222222222222");

        host.Services.GetRequiredService<IRebalanceScheduler>().Enqueue(accountId, handle);

        var jobs = host.Services.GetRequiredService<JobStorage>()
            .GetMonitoringApi()
            .EnqueuedJobs("rebalance", 0, 20);

        jobs.Count.ShouldBe(1);
        var job = jobs.Single().Value.Job;
        job.Type.Name.ShouldBe("RebalanceJob");
        job.Method.Name.ShouldBe("Execute");
        job.Args.ShouldBe([accountId, handle]);
    }

    private static IHost BuildHost()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddHangfire(configuration => configuration.UseInMemoryStorage());
        builder.Services.AddSingleton<IRebalanceScheduler, HangfireRebalanceScheduler>();
        return builder.Build();
    }
}
