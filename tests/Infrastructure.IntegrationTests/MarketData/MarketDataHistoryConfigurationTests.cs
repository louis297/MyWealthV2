using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MyWealthV2.Application.MarketData;
using MyWealthV2.Infrastructure.MarketData;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.MarketData;

/// <summary>Market data history R1, R3: the floor date and the EODHD reach, checked before <c>worker</c> listens.</summary>
public class MarketDataHistoryConfigurationTests
{
    [Test]
    public void NotSet_Starts_WithNoFloorDateAndTheDefaultDays()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.AddMarketDataHistory();

        using var host = builder.Build();
        var settings = host.Services.GetRequiredService<MarketDataHistorySettings>();
        settings.FloorDate.ShouldBeNull();
        settings.HistoryDays.ShouldBe(360);
    }

    [Test]
    public void Set_ReadsTheFloorDateAndTheDays()
    {
        var builder = Builder(("MarketDataHistory:FloorDate", "2026-01-01"), ("Eodhd:HistoryDays", "500"));

        builder.AddMarketDataHistory();

        using var host = builder.Build();
        var settings = host.Services.GetRequiredService<MarketDataHistorySettings>();
        settings.FloorDate.ShouldBe(new DateOnly(2026, 1, 1));
        settings.HistoryDays.ShouldBe(500);
    }

    [Test]
    public void FloorDateOfToday_IsValid()
    {
        var today = new DateOnly(2026, 11, 25);
        var configuration = Configuration(("MarketDataHistory:FloorDate", "2026-11-25"));

        MarketDataHistoryConfiguration.Read(configuration, today).FloorDate.ShouldBe(today);
    }

    [TestCase("not-a-date")]
    [TestCase("2026-13-01")]
    [TestCase("2026-02-30")]
    [TestCase("01/01/2026")]
    [TestCase("2026-1-1")]
    public void FloorDateThatIsNotADate_StopsTheHost(string value)
    {
        var builder = Builder(("MarketDataHistory:FloorDate", value));

        Should.Throw<InvalidOperationException>(() => builder.AddMarketDataHistory())
            .Message.ShouldContain("MarketDataHistory:FloorDate");
    }

    [Test]
    public void FloorDateInTheFuture_StopsTheHost()
    {
        var builder = Builder(("MarketDataHistory:FloorDate", "2999-01-01"));

        Should.Throw<InvalidOperationException>(() => builder.AddMarketDataHistory())
            .Message.ShouldContain("MarketDataHistory:FloorDate");
    }

    [Test]
    public void FloorDateOfTomorrow_IsRefused()
    {
        var configuration = Configuration(("MarketDataHistory:FloorDate", "2026-11-26"));

        Should.Throw<InvalidOperationException>(() =>
            MarketDataHistoryConfiguration.Read(configuration, new DateOnly(2026, 11, 25)));
    }

    [TestCase("0")]
    [TestCase("36501")]
    [TestCase("-1")]
    [TestCase("abc")]
    [TestCase("1.5")]
    public void HistoryDaysOutOfRange_StopsTheHost(string value)
    {
        var builder = Builder(("Eodhd:HistoryDays", value));

        Should.Throw<InvalidOperationException>(() => builder.AddMarketDataHistory())
            .Message.ShouldContain("Eodhd:HistoryDays");
    }

    [TestCase("1", 1)]
    [TestCase("36500", 36500)]
    public void HistoryDaysAtTheEdges_IsValid(string value, int expected)
    {
        MarketDataHistoryConfiguration.Read(Configuration(("Eodhd:HistoryDays", value)), new DateOnly(2026, 11, 25))
            .HistoryDays.ShouldBe(expected);
    }

    private static HostApplicationBuilder Builder(params (string Key, string Value)[] values)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(values.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)));
        return builder;
    }

    private static IConfiguration Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)))
            .Build();
}
