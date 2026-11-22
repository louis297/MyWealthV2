using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using MyWealthV2.Application.Broker;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Broker;
using MyWealthV2.Infrastructure.Identity;
using MyWealthV2.Shared;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.Broker;

/// <summary>Orders R66, R67; ADR 0021 rule 3: the broker ports are dispatched to the adapter a connection names, and
/// every host that registers the broker services can resolve them.</summary>
public class BrokerServicesTests
{
    private static readonly BrokerOrderRequest Order = new(
        "2c0c8e1e-7c7c-4d55-9b53-0b8f5d3f6a11", "ACME.SIM", "USD", OrderSide.Buy, 10m, OrderType.Market, TimeInForce.Day, 105m, 100m);

    [TestCase(BrokerConnectionHost.WebApi)]
    [TestCase(BrokerConnectionHost.Worker)]
    public void AHost_WithItsBrokerConnections_ResolvesTheBrokerPortsAndTheServicesThatUseThem(BrokerConnectionHost kind)
    {
        var builder = Host.CreateApplicationBuilder();
        // Building the host connects to nothing.
        builder.Configuration[$"ConnectionStrings:{Services.Database}"] = "Server=localhost;Database=unused;TrustServerCertificate=True";
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BrokerConnections:simulator:Adapter"] = "Simulator",
            ["BrokerConnections:simulator:BaseUrl"] = "http://simulator:8082",
            ["BrokerConnections:simulator:ApiKey"] = "test-party-api-key-0123456789abcdef"
        });
        builder.AddInfrastructureServices();
        builder.AddBrokerConnections(kind);
        builder.Services.AddScoped<IUser, SystemUser>();

        using var host = builder.Build();
        using var scope = host.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<IBrokerOrders>().ShouldBeOfType<BrokerOrdersDispatcher>();
        scope.ServiceProvider.GetRequiredService<IBrokerFeed>().ShouldBeOfType<BrokerFeedDispatcher>();
        scope.ServiceProvider.GetRequiredService<SendBrokerOrders>().ShouldNotBeNull();
        scope.ServiceProvider.GetRequiredService<PullExecutionReports>().ShouldNotBeNull();
        scope.ServiceProvider.GetRequiredService<ProcessExecutionReports>().ShouldNotBeNull();
    }

    [Test]
    public async Task AConnectionWithNoAdapterOnThisHost_OrThatIsNotConfigured_IsUnreachable()
    {
        var connections = new ConfiguredBrokerConnections([
            new BrokerConnectionSettings("simulator", "Simulator", new Uri("http://simulator:8082"), "key", 10, 1)
        ]);
        var orders = new BrokerOrdersDispatcher(connections, [], NullLogger<BrokerOrdersDispatcher>.Instance);
        var feed = new BrokerFeedDispatcher(connections, [], NullLogger<BrokerFeedDispatcher>.Instance);

        foreach (var connection in new[] { "simulator", "other-broker" })
        {
            (await orders.PlaceAsync(connection, Order, CancellationToken.None)).Failure.ShouldBe(BrokerFailure.Unreachable);
            (await orders.CancelAsync(connection, Order.ClientOrderId, CancellationToken.None)).Failure.ShouldBe(BrokerFailure.Unreachable);
            (await feed.GetReportsAsync(connection, null, 100, CancellationToken.None)).Failure.ShouldBe(BrokerFailure.Unreachable);
        }
    }
}
