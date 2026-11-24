using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MyWealthV2.Application.Custodian;
using MyWealthV2.Infrastructure.Custodian;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.Custodian;

/// <summary>In-kind transfers R2: custodian connections in configuration, checked before the host listens.</summary>
public class CustodianConnectionsConfigurationTests
{
    private const string Key = "test-party-api-key-0123456789abcdef";

    [Test]
    public void NoConnections_IsValid_AndRegistersNone()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.AddCustodianConnections(CustodianConnectionHost.Worker);

        using var host = builder.Build();
        host.Services.GetRequiredService<ICustodianConnections>().All.ShouldBeEmpty();
    }

    [Test]
    public void WebApi_NeedsOnlyTheAdapter_AndListsNameAndAdapter()
    {
        var builder = Builder(new() { ["CustodianConnections:simulator:Adapter"] = "Simulator" });

        builder.AddCustodianConnections(CustodianConnectionHost.WebApi);

        using var host = builder.Build();
        var connections = host.Services.GetRequiredService<ICustodianConnections>();
        connections.All.ShouldBe([new CustodianConnectionInfo("simulator", "Simulator")]);
        connections.IsConfigured("simulator").ShouldBeTrue();
        connections.IsConfigured("other").ShouldBeFalse();
        connections.IsConfigured(null).ShouldBeFalse();
    }

    [Test]
    public void Worker_ReadsUrlKeyAndDefaults()
    {
        var builder = Builder(Worker("simulator"));

        builder.AddCustodianConnections(CustodianConnectionHost.Worker);

        using var host = builder.Build();
        var settings = host.Services.GetRequiredService<ConfiguredCustodianConnections>().Settings.ShouldHaveSingleItem();
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

        values["CustodianConnections:alpha:TimeoutSeconds"] = "60";
        values["CustodianConnections:alpha:PollMinutes"] = "15";
        var builder = Builder(values);

        builder.AddCustodianConnections(CustodianConnectionHost.Worker);

        using var host = builder.Build();
        host.Services.GetRequiredService<ICustodianConnections>().All.Select(entry => entry.Name).ShouldBe(["alpha", "zeta"]);
        var alpha = host.Services.GetRequiredService<ConfiguredCustodianConnections>().Settings.Single(entry => entry.Name == "alpha");
        alpha.TimeoutSeconds.ShouldBe(60);
        alpha.PollMinutes.ShouldBe(15);
    }

    [TestCase("Simulator")]
    [TestCase("1bank")]
    [TestCase("-bank")]
    [TestCase("bank_one")]
    [TestCase("banknamebanknamebanknamebanknamebanknamebanknamebk1")]
    public void BadName_StopsTheHost(string name)
    {
        var builder = Builder(new() { [$"CustodianConnections:{name}:Adapter"] = "Simulator" });

        Should.Throw<InvalidOperationException>(() => builder.AddCustodianConnections(CustodianConnectionHost.WebApi))
            .Message.ShouldContain("CustodianConnections");
    }

    [TestCase(CustodianConnectionHost.WebApi)]
    [TestCase(CustodianConnectionHost.Worker)]
    public void UnknownOrMissingAdapter_StopsTheHost(CustodianConnectionHost host)
    {
        var unknown = Worker("simulator");
        unknown["CustodianConnections:simulator:Adapter"] = "RealCustodian";
        var missing = Worker("simulator");
        missing.Remove("CustodianConnections:simulator:Adapter");

        Should.Throw<InvalidOperationException>(() => Builder(unknown).AddCustodianConnections(host));
        Should.Throw<InvalidOperationException>(() => Builder(missing).AddCustodianConnections(host));
    }

    [TestCase("BaseUrl")]
    [TestCase("ApiKey")]
    public void Worker_MissingUrlOrKey_StopsTheHost(string setting)
    {
        var values = Worker("simulator");
        values.Remove($"CustodianConnections:simulator:{setting}");

        Should.Throw<InvalidOperationException>(() => Builder(values).AddCustodianConnections(CustodianConnectionHost.Worker))
            .Message.ShouldContain(setting);
    }

    [Test]
    public void WebApi_DoesNotNeedUrlOrKey()
    {
        var builder = Builder(new() { ["CustodianConnections:simulator:Adapter"] = "Simulator" });

        Should.NotThrow(() => builder.AddCustodianConnections(CustodianConnectionHost.WebApi));
    }

    [TestCase("0")]
    [TestCase("61")]
    [TestCase("ten")]
    public void Worker_TimeoutOutsideItsRange_StopsTheHost(string value)
    {
        var values = Worker("simulator");
        values["CustodianConnections:simulator:TimeoutSeconds"] = value;

        Should.Throw<InvalidOperationException>(() => Builder(values).AddCustodianConnections(CustodianConnectionHost.Worker))
            .Message.ShouldContain("TimeoutSeconds");
    }

    [TestCase("0")]
    [TestCase("7")]
    [TestCase("90")]
    [TestCase("one")]
    public void Worker_PollMinutesOutsideItsValues_StopsTheHost(string value)
    {
        var values = Worker("simulator");
        values["CustodianConnections:simulator:PollMinutes"] = value;

        Should.Throw<InvalidOperationException>(() => Builder(values).AddCustodianConnections(CustodianConnectionHost.Worker))
            .Message.ShouldContain("PollMinutes");
    }

    [Test]
    public void Worker_BaseUrlThatIsNotAbsoluteHttp_StopsTheHost()
    {
        var values = Worker("simulator");
        values["CustodianConnections:simulator:BaseUrl"] = "simulator:8082";

        Should.Throw<InvalidOperationException>(() => Builder(values).AddCustodianConnections(CustodianConnectionHost.Worker))
            .Message.ShouldContain("BaseUrl");
    }

    private static Dictionary<string, string?> Worker(string name) => new()
    {
        [$"CustodianConnections:{name}:Adapter"] = "Simulator",
        [$"CustodianConnections:{name}:BaseUrl"] = "http://simulator:8082",
        [$"CustodianConnections:{name}:ApiKey"] = Key
    };

    private static HostApplicationBuilder Builder(Dictionary<string, string?> values)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(values);
        return builder;
    }
}
