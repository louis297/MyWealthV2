using System.Text.Json;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MyWealthV2.Infrastructure.MarketData;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.MarketData;

public class InstrumentPriceRecurringJobTests
{
    [Test]
    public void Register_AddsTheHourlyUtcJob()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddHangfire(configuration => configuration.UseInMemoryStorage());
        using var host = builder.Build();

        InstrumentPriceRecurringJob.Register(host.Services.GetRequiredService<IRecurringJobManager>());

        using var connection = host.Services.GetRequiredService<JobStorage>().GetConnection();
        var ids = connection.GetAllItemsFromSet("recurring-jobs");
        ids.ShouldBe(["instrument-price-sync"]);
        var hash = connection.GetAllEntriesFromHash("recurring-job:instrument-price-sync");
        hash["Cron"].ShouldBe("0 * * * *");
        hash["TimeZoneId"].ShouldBe("UTC");
        using var payload = JsonDocument.Parse(hash["Job"]);
        payload.RootElement.GetProperty("Queue").GetString().ShouldBe("prices");
        payload.RootElement.GetProperty("Type").GetString()!.ShouldContain("InstrumentPriceSyncJob");
        payload.RootElement.GetProperty("Method").GetString().ShouldBe("Sync");
    }
}
