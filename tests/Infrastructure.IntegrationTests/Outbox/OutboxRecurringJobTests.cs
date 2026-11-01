using System.Text.Json;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MyWealthV2.Infrastructure.Data;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.Outbox;

public class OutboxRecurringJobTests
{
    [Test]
    public void Register_AddsTheOutboxJobEveryTenSeconds()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddHangfire(configuration => configuration.UseInMemoryStorage());
        using var host = builder.Build();

        OutboxRecurringJob.Register(host.Services.GetRequiredService<IRecurringJobManager>());

        using var connection = host.Services.GetRequiredService<JobStorage>().GetConnection();
        var ids = connection.GetAllItemsFromSet("recurring-jobs");
        ids.ShouldBe(["outbox"]);
        var hash = connection.GetAllEntriesFromHash("recurring-job:outbox");
        hash["Cron"].ShouldBe("*/10 * * * * *");
        using var payload = JsonDocument.Parse(hash["Job"]);
        payload.RootElement.GetProperty("Queue").GetString().ShouldBe("outbox");
        payload.RootElement.GetProperty("Type").GetString()!.ShouldContain("OutboxProcessor");
        payload.RootElement.GetProperty("Method").GetString().ShouldBe("Process");
    }
}
