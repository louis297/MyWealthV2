using Microsoft.Extensions.Configuration;
using MyWealthV2.Application.BankFeed;

namespace MyWealthV2.Infrastructure.BankFeed;

/// <summary>Which host reads the configuration: only <c>worker</c> holds the URL and the secret (deposit-matching R1).</summary>
public enum BankConnectionHost
{
    WebApi,
    Worker
}

/// <summary>One configured bank connection (deposit-matching R1). <see cref="BaseUrl"/> and <see cref="ApiKey"/>
/// are set only on <c>worker</c>.</summary>
public sealed record BankConnectionSettings(
    string Name,
    string Adapter,
    Uri? BaseUrl,
    string? ApiKey,
    int TimeoutSeconds,
    int PollMinutes);

/// <summary>
/// Reads <c>BankConnections:{name}</c> (deposit-matching R1). Throws <see cref="InvalidOperationException"/> on a bad
/// entry, so a host that calls it before <c>Build()</c> stops before it listens. Reads the configuration, never the
/// environment name.
/// </summary>
public static class BankConnectionsConfiguration
{
    public const string SectionName = "BankConnections";
    public const string SimulatorAdapter = "Simulator";
    public const int DefaultTimeoutSeconds = 10;
    public const int DefaultPollMinutes = 1;

    /// <summary>Every value divides an hour, so every run starts on the same minutes of each hour.</summary>
    public static readonly IReadOnlyList<int> AllowedPollMinutes = [1, 2, 3, 4, 5, 6, 10, 12, 15, 20, 30, 60];

    public static IReadOnlyList<BankConnectionSettings> Read(IConfiguration configuration, BankConnectionHost host) =>
        throw new NotImplementedException();
}

/// <summary>The configured connections, as the Application sees them.</summary>
public sealed class ConfiguredBankConnections(IReadOnlyList<BankConnectionSettings> settings) : IBankConnections
{
    public IReadOnlyList<BankConnectionSettings> Settings { get; } = settings;

    public IReadOnlyList<BankConnectionInfo> All { get; } = settings
        .OrderBy(entry => entry.Name, StringComparer.Ordinal)
        .Select(entry => new BankConnectionInfo(entry.Name, entry.Adapter))
        .ToList();

    public bool IsConfigured(string? name) => All.Any(entry => entry.Name == name);
}
