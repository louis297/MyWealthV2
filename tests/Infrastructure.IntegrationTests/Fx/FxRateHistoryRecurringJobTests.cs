using System.Text.Json;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MyWealthV2.Infrastructure.MarketData;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.Fx;

/// <summary>Market data history R12: <c>fx-rate-history-sync</c> runs daily at 11:30 UTC on queue <c>fx</c>.</summary>
public class FxRateHistoryRecurringJobTests
{
    [Test]
    public void Register_AddsTheDailyUtcJobOnQueueFx()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddHangfire(configuration => configuration.UseInMemoryStorage());
        using var host = builder.Build();

        FxRateHistoryRecurringJob.Register(host.Services.GetRequiredService<IRecurringJobManager>());

        using var connection = host.Services.GetRequiredService<JobStorage>().GetConnection();
        connection.GetAllItemsFromSet("recurring-jobs").ShouldBe(["fx-rate-history-sync"]);
        var hash = connection.GetAllEntriesFromHash("recurring-job:fx-rate-history-sync");
        hash["Cron"].ShouldBe("30 11 * * *");
        hash["TimeZoneId"].ShouldBe("UTC");
        using var payload = JsonDocument.Parse(hash["Job"]);
        payload.RootElement.GetProperty("Queue").GetString().ShouldBe("fx");
        payload.RootElement.GetProperty("Type").GetString()!.ShouldContain("FxRateHistoryHangfireJob");
        payload.RootElement.GetProperty("Method").GetString().ShouldBe("Run");
    }
}
