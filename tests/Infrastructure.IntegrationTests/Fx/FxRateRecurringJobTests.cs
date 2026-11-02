using System.Text.Json;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MyWealthV2.Infrastructure.MarketData;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.Fx;

public class FxRateRecurringJobTests
{
    [Test]
    public void Register_AddsTheHourlyUtcJob()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddHangfire(configuration => configuration.UseInMemoryStorage());
        using var host = builder.Build();

        FxRateRecurringJob.Register(host.Services.GetRequiredService<IRecurringJobManager>());

        using var connection = host.Services.GetRequiredService<JobStorage>().GetConnection();
        var ids = connection.GetAllItemsFromSet("recurring-jobs");
        ids.ShouldBe(["fx-rate-sync"]);
        var hash = connection.GetAllEntriesFromHash("recurring-job:fx-rate-sync");
        hash["Cron"].ShouldBe("0 * * * *");
        hash["TimeZoneId"].ShouldBe("UTC");
        using var payload = JsonDocument.Parse(hash["Job"]);
        payload.RootElement.GetProperty("Queue").GetString().ShouldBe("fx");
        payload.RootElement.GetProperty("Type").GetString()!.ShouldContain("FxRateSyncJob");
        payload.RootElement.GetProperty("Method").GetString().ShouldBe("Sync");
    }
}
