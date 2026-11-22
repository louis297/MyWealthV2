using System.Globalization;
using Microsoft.Extensions.Configuration;
using MyWealthV2.Application.Broker;
using MyWealthV2.Domain.ValueObjects;

namespace MyWealthV2.Infrastructure.Broker;

/// <summary>Which host reads the configuration: only <c>worker</c> holds the URL and the secret (orders R2).</summary>
public enum BrokerConnectionHost
{
    WebApi,
    Worker
}

/// <summary>One configured broker connection (orders R2). <see cref="BaseUrl"/> and <see cref="ApiKey"/> are set only
/// on <c>worker</c>.</summary>
public sealed record BrokerConnectionSettings(
    string Name,
    string Adapter,
    Uri? BaseUrl,
    string? ApiKey,
    int TimeoutSeconds,
    int PollMinutes);

/// <summary>
/// Reads <c>BrokerConnections:{name}</c> (orders R2). Throws <see cref="InvalidOperationException"/> on a bad entry, so
/// a host that calls it before <c>Build()</c> stops before it listens. Reads the configuration, never the environment
/// name (ADR 0021 rule 3). A name follows the rule of a bank connection name.
/// </summary>
public static class BrokerConnectionsConfiguration
{
    public const string SectionName = "BrokerConnections";
    public const string SimulatorAdapter = "Simulator";
    public const int DefaultTimeoutSeconds = 10;
    public const int DefaultPollMinutes = 1;

    /// <summary>Every value divides an hour, so every run starts on the same minutes of each hour.</summary>
    public static readonly IReadOnlyList<int> AllowedPollMinutes = [1, 2, 3, 4, 5, 6, 10, 12, 15, 20, 30, 60];

    public static IReadOnlyList<BrokerConnectionSettings> Read(IConfiguration configuration, BrokerConnectionHost host) => [];
}

/// <summary>The configured connections, as the Application sees them.</summary>
public sealed class ConfiguredBrokerConnections(IReadOnlyList<BrokerConnectionSettings> settings) : IBrokerConnections
{
    public IReadOnlyList<BrokerConnectionSettings> Settings { get; } = settings;

    public IReadOnlyList<BrokerConnectionInfo> All { get; } = settings
        .OrderBy(entry => entry.Name, StringComparer.Ordinal)
        .Select(entry => new BrokerConnectionInfo(entry.Name, entry.Adapter))
        .ToList();

    public bool IsConfigured(string? name) => All.Any(entry => entry.Name == name);
}
