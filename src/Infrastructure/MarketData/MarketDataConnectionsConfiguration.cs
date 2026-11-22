using Microsoft.Extensions.Configuration;
using MyWealthV2.Domain.MarketData;

namespace MyWealthV2.Infrastructure.MarketData;

/// <summary>Which host reads the configuration: only <c>worker</c> holds the URL and the secret
/// (docs/features/simulated-instruments.md R1).</summary>
public enum MarketDataHost
{
    WebApi,
    Worker
}

/// <summary>One configured market data connection (simulated-instruments R1, R2). <see cref="BaseUrl"/> and
/// <see cref="ApiKey"/> are set only on <c>worker</c>.</summary>
public sealed record MarketDataConnectionSettings(
    string Name,
    string Adapter,
    string SymbolSuffix,
    Uri? BaseUrl,
    string? ApiKey,
    int TimeoutSeconds,
    int PollMinutes)
{
    public MarketDataConnection Connection => new(Name, Adapter, SymbolSuffix);
}

/// <summary>
/// Reads <c>MarketDataConnections:{name}</c> (simulated-instruments R1–R3). Throws
/// <see cref="InvalidOperationException"/> on a bad entry, so a host that calls it before <c>Build()</c> stops before
/// it listens. Reads the configuration, never the environment name.
/// </summary>
public static class MarketDataConnectionsConfiguration
{
    public const string SectionName = "MarketDataConnections";
    public const string SimulatorAdapter = "Simulator";
    public const int DefaultTimeoutSeconds = 10;
    public const int DefaultPollMinutes = 1;

    public static IReadOnlyList<MarketDataConnectionSettings> Read(IConfiguration configuration, MarketDataHost host) => [];
}

/// <summary>The configured connections: the full settings for the adapters and jobs, and the domain view.</summary>
public sealed class ConfiguredMarketDataConnections(IReadOnlyList<MarketDataConnectionSettings> settings)
{
    public IReadOnlyList<MarketDataConnectionSettings> Settings { get; } = settings
        .OrderBy(entry => entry.Name, StringComparer.Ordinal)
        .ToList();

    public MarketDataConnections Connections { get; } = new(settings.Select(entry => entry.Connection));
}
