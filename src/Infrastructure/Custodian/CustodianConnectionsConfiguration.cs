using Microsoft.Extensions.Configuration;
using MyWealthV2.Application.Custodian;

namespace MyWealthV2.Infrastructure.Custodian;

/// <summary>Which host reads the configuration: only <c>worker</c> holds the URL and the secret (in-kind-transfers R2).</summary>
public enum CustodianConnectionHost
{
    WebApi,
    Worker
}

/// <summary>One configured custodian connection (in-kind-transfers R2). <see cref="BaseUrl"/> and <see cref="ApiKey"/>
/// are set only on <c>worker</c>.</summary>
public sealed record CustodianConnectionSettings(
    string Name,
    string Adapter,
    Uri? BaseUrl,
    string? ApiKey,
    int TimeoutSeconds,
    int PollMinutes);

/// <summary>
/// Reads <c>CustodianConnections:{name}</c> (in-kind-transfers R2, the rules of deposit-matching R1). Throws
/// <see cref="InvalidOperationException"/> on a bad entry, so a host that calls it before <c>Build()</c> stops before it
/// listens.
/// </summary>
public static class CustodianConnectionsConfiguration
{
    public const string SectionName = "CustodianConnections";
    public const string SimulatorAdapter = "Simulator";
    public const int DefaultTimeoutSeconds = 10;
    public const int DefaultPollMinutes = 1;

    public static readonly IReadOnlyList<int> AllowedPollMinutes = [1, 2, 3, 4, 5, 6, 10, 12, 15, 20, 30, 60];

    public static IReadOnlyList<CustodianConnectionSettings> Read(IConfiguration configuration, CustodianConnectionHost host) => [];
}

/// <summary>The configured connections, as the Application sees them.</summary>
public sealed class ConfiguredCustodianConnections(IReadOnlyList<CustodianConnectionSettings> settings) : ICustodianConnections
{
    public IReadOnlyList<CustodianConnectionSettings> Settings { get; } = settings;

    public IReadOnlyList<CustodianConnectionInfo> All { get; } = settings
        .OrderBy(entry => entry.Name, StringComparer.Ordinal)
        .Select(entry => new CustodianConnectionInfo(entry.Name, entry.Adapter))
        .ToList();

    public bool IsConfigured(string? name) => All.Any(entry => entry.Name == name);

    public CustodianConnectionSettings? Find(string? name) => Settings.FirstOrDefault(entry => entry.Name == name);
}
