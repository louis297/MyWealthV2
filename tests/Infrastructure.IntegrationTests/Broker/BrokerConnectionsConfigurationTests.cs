using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MyWealthV2.Application.Broker;
using MyWealthV2.Infrastructure.Broker;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.Broker;

/// <summary>Orders R2: broker connections in configuration, checked before the host listens.</summary>
public class BrokerConnectionsConfigurationTests
{
    private const string Key = "test-party-api-key-0123456789abcdef";

    [Test]
    public void NoConnections_IsValid_AndRegistersNone()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.AddBrokerConnections(BrokerConnectionHost.Worker);

        using var host = builder.Build();
        host.Services.GetRequiredService<IBrokerConnections>().All.ShouldBeEmpty();
    }

    [Test]
    public void WebApi_NeedsOnlyTheAdapter_AndListsNameAndAdapter()
    {
        var builder = Builder(new() { ["BrokerConnections:simulator:Adapter"] = "Simulator" });

        builder.AddBrokerConnections(BrokerConnectionHost.WebApi);

        using var host = builder.Build();
        var connections = host.Services.GetRequiredService<IBrokerConnections>();
        connections.All.ShouldBe([new BrokerConnectionInfo("simulator", "Simulator")]);
        connections.IsConfigured("simulator").ShouldBeTrue();
        connections.IsConfigured("other").ShouldBeFalse();
        connections.IsConfigured(null).ShouldBeFalse();
    }

    [Test]
    public void Worker_ReadsUrlKeyAndDefaults()
    {
        var builder = Builder(Worker("simulator"));

        builder.AddBrokerConnections(BrokerConnectionHost.Worker);

        using var host = builder.Build();
        var settings = host.Services.GetRequiredService<ConfiguredBrokerConnections>().Settings.ShouldHaveSingleItem();
        settings.Name.ShouldBe("simulator");
        settings.Adapter.ShouldBe("Simulator");
        settings.BaseUrl.ShouldBe(new Uri("http://simulator:8082"));
        settings.ApiKey.ShouldBe(Key);
        settings.TimeoutSeconds.ShouldBe(10);
        settings.PollMinutes.ShouldBe(1);
    }

    [Test]
    public void Worker_ReadsTimeoutAndPollMinutes_AndSortsByName()
    {
        var values = Worker("zeta");
        foreach (var (key, value) in Worker("alpha"))
        {
            values[key] = value;
        }

        values["BrokerConnections:alpha:TimeoutSeconds"] = "60";
        values["BrokerConnections:alpha:PollMinutes"] = "15";
        var builder = Builder(values);

        builder.AddBrokerConnections(BrokerConnectionHost.Worker);

        using var host = builder.Build();
        host.Services.GetRequiredService<IBrokerConnections>().All.Select(entry => entry.Name).ShouldBe(["alpha", "zeta"]);
        var alpha = host.Services.GetRequiredService<ConfiguredBrokerConnections>().Settings.Single(entry => entry.Name == "alpha");
        alpha.TimeoutSeconds.ShouldBe(60);
        alpha.PollMinutes.ShouldBe(15);
    }

    [TestCase("Simulator")]
    [TestCase("1broker")]
    [TestCase("-broker")]
    [TestCase("broker_one")]
    [TestCase("banknamebanknamebanknamebanknamebanknamebanknamebk1")]
    public void BadName_StopsTheHost(string name)
    {
        var builder = Builder(new() { [$"BrokerConnections:{name}:Adapter"] = "Simulator" });

        Should.Throw<InvalidOperationException>(() => builder.AddBrokerConnections(BrokerConnectionHost.WebApi))
            .Message.ShouldContain("BrokerConnections");
    }

    [TestCase(BrokerConnectionHost.WebApi)]
    [TestCase(BrokerConnectionHost.Worker)]
    public void UnknownOrMissingAdapter_StopsTheHost(BrokerConnectionHost host)
    {
        var unknown = Worker("simulator");
        unknown["BrokerConnections:simulator:Adapter"] = "RealBroker";
        var missing = Worker("simulator");
        missing.Remove("BrokerConnections:simulator:Adapter");

        Should.Throw<InvalidOperationException>(() => Builder(unknown).AddBrokerConnections(host));
        Should.Throw<InvalidOperationException>(() => Builder(missing).AddBrokerConnections(host));
    }

    [TestCase("BaseUrl")]
    [TestCase("ApiKey")]
    public void Worker_MissingUrlOrKey_StopsTheHost(string setting)
    {
        var values = Worker("simulator");
        values.Remove($"BrokerConnections:simulator:{setting}");

        Should.Throw<InvalidOperationException>(() => Builder(values).AddBrokerConnections(BrokerConnectionHost.Worker))
            .Message.ShouldContain(setting);
    }

    [Test]
    public void WebApi_DoesNotNeedUrlOrKey()
    {
        var builder = Builder(new() { ["BrokerConnections:simulator:Adapter"] = "Simulator" });

        Should.NotThrow(() => builder.AddBrokerConnections(BrokerConnectionHost.WebApi));
    }

    [TestCase("0")]
    [TestCase("61")]
    [TestCase("ten")]
    public void Worker_TimeoutOutsideItsRange_StopsTheHost(string value)
    {
        var values = Worker("simulator");
        values["BrokerConnections:simulator:TimeoutSeconds"] = value;

        Should.Throw<InvalidOperationException>(() => Builder(values).AddBrokerConnections(BrokerConnectionHost.Worker))
            .Message.ShouldContain("TimeoutSeconds");
    }

    [TestCase("0")]
    [TestCase("7")]
    [TestCase("90")]
    [TestCase("one")]
    public void Worker_PollMinutesOutsideItsValues_StopsTheHost(string value)
    {
        var values = Worker("simulator");
        values["BrokerConnections:simulator:PollMinutes"] = value;

        Should.Throw<InvalidOperationException>(() => Builder(values).AddBrokerConnections(BrokerConnectionHost.Worker))
            .Message.ShouldContain("PollMinutes");
    }

    [Test]
    public void Worker_BaseUrlThatIsNotAbsoluteHttp_StopsTheHost()
    {
        var values = Worker("simulator");
        values["BrokerConnections:simulator:BaseUrl"] = "simulator:8082";

        Should.Throw<InvalidOperationException>(() => Builder(values).AddBrokerConnections(BrokerConnectionHost.Worker))
            .Message.ShouldContain("BaseUrl");
    }

    private static Dictionary<string, string?> Worker(string name) => new()
    {
        [$"BrokerConnections:{name}:Adapter"] = "Simulator",
        [$"BrokerConnections:{name}:BaseUrl"] = "http://simulator:8082",
        [$"BrokerConnections:{name}:ApiKey"] = Key
    };

    private static HostApplicationBuilder Builder(Dictionary<string, string?> values)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(values);
        return builder;
    }
}
