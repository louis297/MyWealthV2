using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MyWealthV2.Domain.MarketData;
using MyWealthV2.Infrastructure.MarketData;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.MarketData;

/// <summary>Simulated instruments R1–R3: market data connections in configuration, checked before the host listens.</summary>
public class MarketDataConnectionsConfigurationTests
{
    private const string Key = "test-party-api-key-0123456789abcdef";

    [TestCase(MarketDataHost.WebApi)]
    [TestCase(MarketDataHost.Worker)]
    public void NoConnections_IsValid_AndServesNothing(MarketDataHost host)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.AddMarketDataConnections(host);

        using var built = builder.Build();
        var connections = built.Services.GetRequiredService<MarketDataConnections>();
        connections.All.ShouldBeEmpty();
        connections.For("ACME.SIM").ShouldBeNull();
    }

    [Test]
    public void WebApi_NeedsOnlyTheAdapterAndTheSuffix()
    {
        var builder = Builder(WebApi("simulator", ".SIM"));

        builder.AddMarketDataConnections(MarketDataHost.WebApi);

        using var host = builder.Build();
        var connection = host.Services.GetRequiredService<MarketDataConnections>().All.ShouldHaveSingleItem();
        connection.ShouldBe(new MarketDataConnection("simulator", "Simulator", ".SIM"));
        host.Services.GetRequiredService<MarketDataConnections>().For("acme.sim").ShouldBe(connection);
    }

    [Test]
    public void Worker_ReadsUrlKeyAndDefaults()
    {
        var builder = Builder(Worker("simulator", ".SIM"));

        builder.AddMarketDataConnections(MarketDataHost.Worker);

        using var host = builder.Build();
        var settings = host.Services.GetRequiredService<ConfiguredMarketDataConnections>().Settings.ShouldHaveSingleItem();
        settings.Name.ShouldBe("simulator");
        settings.Adapter.ShouldBe("Simulator");
        settings.SymbolSuffix.ShouldBe(".SIM");
        settings.BaseUrl.ShouldBe(new Uri("http://simulator:8082"));
        settings.ApiKey.ShouldBe(Key);
        settings.TimeoutSeconds.ShouldBe(10);
        settings.PollMinutes.ShouldBe(1);
    }

    [Test]
    public void Worker_ReadsTimeoutAndPollMinutes_AndSortsByName()
    {
        var values = Worker("zeta", ".ZET");
        foreach (var (key, value) in Worker("alpha", ".ALP"))
        {
            values[key] = value;
        }

        values["MarketDataConnections:alpha:TimeoutSeconds"] = "60";
        values["MarketDataConnections:alpha:PollMinutes"] = "15";
        var builder = Builder(values);

        builder.AddMarketDataConnections(MarketDataHost.Worker);

        using var host = builder.Build();
        var settings = host.Services.GetRequiredService<ConfiguredMarketDataConnections>().Settings;
        settings.Select(entry => entry.Name).ShouldBe(["alpha", "zeta"]);
        settings[0].TimeoutSeconds.ShouldBe(60);
        settings[0].PollMinutes.ShouldBe(15);
    }

    [TestCase("Simulator")]
    [TestCase("1market")]
    [TestCase("-market")]
    [TestCase("market_one")]
    [TestCase("marketnamemarketnamemarketnamemarketnamemarketname1")]
    public void BadName_StopsTheHost(string name)
    {
        var builder = Builder(WebApi(name, ".SIM"));

        Should.Throw<InvalidOperationException>(() => builder.AddMarketDataConnections(MarketDataHost.WebApi))
            .Message.ShouldContain("MarketDataConnections");
    }

    [TestCase(MarketDataHost.WebApi)]
    [TestCase(MarketDataHost.Worker)]
    public void UnknownOrMissingAdapter_StopsTheHost(MarketDataHost host)
    {
        var unknown = Worker("simulator", ".SIM");
        unknown["MarketDataConnections:simulator:Adapter"] = "Eodhd";
        var missing = Worker("simulator", ".SIM");
        missing.Remove("MarketDataConnections:simulator:Adapter");

        Should.Throw<InvalidOperationException>(() => Builder(unknown).AddMarketDataConnections(host))
            .Message.ShouldContain("Adapter");
        Should.Throw<InvalidOperationException>(() => Builder(missing).AddMarketDataConnections(host))
            .Message.ShouldContain("Adapter");
    }

    [TestCase(MarketDataHost.WebApi, null)]
    [TestCase(MarketDataHost.WebApi, "SIM")]
    [TestCase(MarketDataHost.WebApi, ".S")]
    [TestCase(MarketDataHost.WebApi, ".ABCDEFGHIJK")]
    [TestCase(MarketDataHost.Worker, ".SI-M")]
    [TestCase(MarketDataHost.Worker, "")]
    public void MissingOrBadSuffix_StopsTheHost(MarketDataHost host, string? suffix)
    {
        var values = Worker("simulator", ".SIM");
        values["MarketDataConnections:simulator:SymbolSuffix"] = suffix;

        Should.Throw<InvalidOperationException>(() => Builder(values).AddMarketDataConnections(host))
            .Message.ShouldContain("SymbolSuffix");
    }

    [TestCase(MarketDataHost.WebApi)]
    [TestCase(MarketDataHost.Worker)]
    public void ASuffixUsedTwice_StopsTheHost_WithoutRegardToCase(MarketDataHost host)
    {
        var values = Worker("alpha", ".SIM");
        foreach (var (key, value) in Worker("beta", ".sim"))
        {
            values[key] = value;
        }

        Should.Throw<InvalidOperationException>(() => Builder(values).AddMarketDataConnections(host))
            .Message.ShouldContain("SymbolSuffix");
    }

    [TestCase("BaseUrl")]
    [TestCase("ApiKey")]
    public void Worker_MissingUrlOrKey_StopsTheHost(string setting)
    {
        var values = Worker("simulator", ".SIM");
        values.Remove($"MarketDataConnections:simulator:{setting}");

        Should.Throw<InvalidOperationException>(() => Builder(values).AddMarketDataConnections(MarketDataHost.Worker))
            .Message.ShouldContain(setting);
    }

    [Test]
    public void Worker_BaseUrlThatIsNotAbsoluteHttp_StopsTheHost()
    {
        var values = Worker("simulator", ".SIM");
        values["MarketDataConnections:simulator:BaseUrl"] = "simulator:8082";

        Should.Throw<InvalidOperationException>(() => Builder(values).AddMarketDataConnections(MarketDataHost.Worker))
            .Message.ShouldContain("BaseUrl");
    }

    [Test]
    public void WebApi_DoesNotNeedUrlOrKey()
    {
        var builder = Builder(WebApi("simulator", ".SIM"));

        Should.NotThrow(() => builder.AddMarketDataConnections(MarketDataHost.WebApi));
    }

    [TestCase("0")]
    [TestCase("61")]
    [TestCase("ten")]
    public void Worker_TimeoutOutsideItsRange_StopsTheHost(string value)
    {
        var values = Worker("simulator", ".SIM");
        values["MarketDataConnections:simulator:TimeoutSeconds"] = value;

        Should.Throw<InvalidOperationException>(() => Builder(values).AddMarketDataConnections(MarketDataHost.Worker))
            .Message.ShouldContain("TimeoutSeconds");
    }

    [TestCase("0")]
    [TestCase("7")]
    [TestCase("90")]
    [TestCase("one")]
    public void Worker_PollMinutesOutsideItsValues_StopsTheHost(string value)
    {
        var values = Worker("simulator", ".SIM");
        values["MarketDataConnections:simulator:PollMinutes"] = value;

        Should.Throw<InvalidOperationException>(() => Builder(values).AddMarketDataConnections(MarketDataHost.Worker))
            .Message.ShouldContain("PollMinutes");
    }

    private static Dictionary<string, string?> WebApi(string name, string suffix) => new()
    {
        [$"MarketDataConnections:{name}:Adapter"] = "Simulator",
        [$"MarketDataConnections:{name}:SymbolSuffix"] = suffix
    };

    private static Dictionary<string, string?> Worker(string name, string suffix)
    {
        var values = WebApi(name, suffix);
        values[$"MarketDataConnections:{name}:BaseUrl"] = "http://simulator:8082";
        values[$"MarketDataConnections:{name}:ApiKey"] = Key;
        return values;
    }

    private static HostApplicationBuilder Builder(Dictionary<string, string?> values)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(values);
        return builder;
    }
}
