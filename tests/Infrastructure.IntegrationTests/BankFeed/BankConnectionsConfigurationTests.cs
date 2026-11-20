using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MyWealthV2.Application.BankFeed;
using MyWealthV2.Infrastructure.BankFeed;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.BankFeed;

/// <summary>Deposit matching R1: bank connections in configuration, checked before the host listens.</summary>
public class BankConnectionsConfigurationTests
{
    private const string Key = "test-party-api-key-0123456789abcdef";

    [Test]
    public void NoConnections_IsValid_AndRegistersNone()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.AddBankConnections(BankConnectionHost.Worker);

        using var host = builder.Build();
        host.Services.GetRequiredService<IBankConnections>().All.ShouldBeEmpty();
    }

    [Test]
    public void WebApi_NeedsOnlyTheAdapter_AndListsNameAndAdapter()
    {
        var builder = Builder(new() { ["BankConnections:simulator:Adapter"] = "Simulator" });

        builder.AddBankConnections(BankConnectionHost.WebApi);

        using var host = builder.Build();
        var connections = host.Services.GetRequiredService<IBankConnections>();
        connections.All.ShouldBe([new BankConnectionInfo("simulator", "Simulator")]);
        connections.IsConfigured("simulator").ShouldBeTrue();
        connections.IsConfigured("other").ShouldBeFalse();
        connections.IsConfigured(null).ShouldBeFalse();
    }

    [Test]
    public void Worker_ReadsUrlKeyAndDefaults()
    {
        var builder = Builder(Worker("simulator"));

        builder.AddBankConnections(BankConnectionHost.Worker);

        using var host = builder.Build();
        var settings = host.Services.GetRequiredService<ConfiguredBankConnections>().Settings.ShouldHaveSingleItem();
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

        values["BankConnections:alpha:TimeoutSeconds"] = "60";
        values["BankConnections:alpha:PollMinutes"] = "15";
        var builder = Builder(values);

        builder.AddBankConnections(BankConnectionHost.Worker);

        using var host = builder.Build();
        host.Services.GetRequiredService<IBankConnections>().All.Select(entry => entry.Name).ShouldBe(["alpha", "zeta"]);
        var alpha = host.Services.GetRequiredService<ConfiguredBankConnections>().Settings.Single(entry => entry.Name == "alpha");
        alpha.TimeoutSeconds.ShouldBe(60);
        alpha.PollMinutes.ShouldBe(15);
    }

    [TestCase("Simulator")]
    [TestCase("1bank")]
    [TestCase("-bank")]
    [TestCase("bank_one")]
    [TestCase("banknamebanknamebanknamebanknamebanknamebanknameb1")]
    public void BadName_StopsTheHost(string name)
    {
        var builder = Builder(new() { [$"BankConnections:{name}:Adapter"] = "Simulator" });

        Should.Throw<InvalidOperationException>(() => builder.AddBankConnections(BankConnectionHost.WebApi))
            .Message.ShouldContain("BankConnections");
    }

    [TestCase(BankConnectionHost.WebApi)]
    [TestCase(BankConnectionHost.Worker)]
    public void UnknownOrMissingAdapter_StopsTheHost(BankConnectionHost host)
    {
        var unknown = Worker("simulator");
        unknown["BankConnections:simulator:Adapter"] = "RealBank";
        var missing = Worker("simulator");
        missing.Remove("BankConnections:simulator:Adapter");

        Should.Throw<InvalidOperationException>(() => Builder(unknown).AddBankConnections(host));
        Should.Throw<InvalidOperationException>(() => Builder(missing).AddBankConnections(host));
    }

    [TestCase("BaseUrl")]
    [TestCase("ApiKey")]
    public void Worker_MissingUrlOrKey_StopsTheHost(string setting)
    {
        var values = Worker("simulator");
        values.Remove($"BankConnections:simulator:{setting}");

        Should.Throw<InvalidOperationException>(() => Builder(values).AddBankConnections(BankConnectionHost.Worker))
            .Message.ShouldContain(setting);
    }

    [Test]
    public void WebApi_DoesNotNeedUrlOrKey()
    {
        var builder = Builder(new() { ["BankConnections:simulator:Adapter"] = "Simulator" });

        Should.NotThrow(() => builder.AddBankConnections(BankConnectionHost.WebApi));
    }

    [TestCase("0")]
    [TestCase("61")]
    [TestCase("ten")]
    public void Worker_TimeoutOutsideItsRange_StopsTheHost(string value)
    {
        var values = Worker("simulator");
        values["BankConnections:simulator:TimeoutSeconds"] = value;

        Should.Throw<InvalidOperationException>(() => Builder(values).AddBankConnections(BankConnectionHost.Worker))
            .Message.ShouldContain("TimeoutSeconds");
    }

    [TestCase("0")]
    [TestCase("7")]
    [TestCase("90")]
    [TestCase("one")]
    public void Worker_PollMinutesOutsideItsValues_StopsTheHost(string value)
    {
        var values = Worker("simulator");
        values["BankConnections:simulator:PollMinutes"] = value;

        Should.Throw<InvalidOperationException>(() => Builder(values).AddBankConnections(BankConnectionHost.Worker))
            .Message.ShouldContain("PollMinutes");
    }

    [Test]
    public void Worker_BaseUrlThatIsNotAbsoluteHttp_StopsTheHost()
    {
        var values = Worker("simulator");
        values["BankConnections:simulator:BaseUrl"] = "simulator:8082";

        Should.Throw<InvalidOperationException>(() => Builder(values).AddBankConnections(BankConnectionHost.Worker))
            .Message.ShouldContain("BaseUrl");
    }

    private static Dictionary<string, string?> Worker(string name) => new()
    {
        [$"BankConnections:{name}:Adapter"] = "Simulator",
        [$"BankConnections:{name}:BaseUrl"] = "http://simulator:8082",
        [$"BankConnections:{name}:ApiKey"] = Key
    };

    private static HostApplicationBuilder Builder(Dictionary<string, string?> values)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(values);
        return builder;
    }
}
