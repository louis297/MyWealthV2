using System.Globalization;
using Microsoft.Extensions.Configuration;
using MyWealthV2.Domain.ValueObjects;
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

    public static IReadOnlyList<CustodianConnectionSettings> Read(IConfiguration configuration, CustodianConnectionHost host)
    {
        var result = new List<CustodianConnectionSettings>();
        foreach (var section in configuration.GetSection(SectionName).GetChildren())
        {
            var name = section.Key;
            if (!BankConnectionName.IsValid(name))
            {
                throw Bad(name, "the name must be 1 to 50 characters: a lower-case letter, then lower-case letters, digits and hyphens");
            }

            if (!string.Equals(section["Adapter"], SimulatorAdapter, StringComparison.Ordinal))
            {
                throw Bad(name, $"Adapter must be {SimulatorAdapter}");
            }

            if (host == CustodianConnectionHost.WebApi)
            {
                result.Add(new CustodianConnectionSettings(name, SimulatorAdapter, null, null, DefaultTimeoutSeconds, DefaultPollMinutes));
                continue;
            }

            if (!Uri.TryCreate(section["BaseUrl"], UriKind.Absolute, out var baseUrl) || baseUrl.Scheme is not ("http" or "https"))
            {
                throw Bad(name, "BaseUrl must be an absolute http or https URL");
            }

            var apiKey = section["ApiKey"];
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw Bad(name, "ApiKey must be set");
            }

            var timeoutSeconds = ReadInteger(section, name, "TimeoutSeconds", DefaultTimeoutSeconds);
            if (timeoutSeconds is < 1 or > 60)
            {
                throw Bad(name, "TimeoutSeconds must be an integer from 1 to 60");
            }

            var pollMinutes = ReadInteger(section, name, "PollMinutes", DefaultPollMinutes);
            if (!AllowedPollMinutes.Contains(pollMinutes))
            {
                throw Bad(name, $"PollMinutes must be one of {string.Join(", ", AllowedPollMinutes)}");
            }

            result.Add(new CustodianConnectionSettings(name, SimulatorAdapter, baseUrl, apiKey, timeoutSeconds, pollMinutes));
        }

        return result.OrderBy(entry => entry.Name, StringComparer.Ordinal).ToList();
    }

    private static int ReadInteger(IConfigurationSection section, string name, string key, int defaultValue)
    {
        var value = section[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw Bad(name, $"{key} must be an integer");
    }

    private static InvalidOperationException Bad(string name, string problem) =>
        new($"{SectionName}:{name} is not valid: {problem}.");
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
