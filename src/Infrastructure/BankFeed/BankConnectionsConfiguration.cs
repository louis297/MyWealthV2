using System.Globalization;
using Microsoft.Extensions.Configuration;
using MyWealthV2.Application.BankFeed;
using MyWealthV2.Domain.ValueObjects;

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

    public static IReadOnlyList<BankConnectionSettings> Read(IConfiguration configuration, BankConnectionHost host)
    {
        var result = new List<BankConnectionSettings>();
        foreach (var section in configuration.GetSection(SectionName).GetChildren())
        {
            var name = section.Key;
            if (!BankConnectionName.IsValid(name))
            {
                throw Bad(name, "the name must be 1 to 50 characters: a lower-case letter, then lower-case letters, digits and hyphens");
            }

            var adapter = section["Adapter"];
            if (!string.Equals(adapter, SimulatorAdapter, StringComparison.Ordinal))
            {
                throw Bad(name, $"Adapter must be {SimulatorAdapter}");
            }

            if (host == BankConnectionHost.WebApi)
            {
                result.Add(new BankConnectionSettings(name, SimulatorAdapter, null, null, DefaultTimeoutSeconds, DefaultPollMinutes));
                continue;
            }

            if (!Uri.TryCreate(section["BaseUrl"], UriKind.Absolute, out var baseUrl)
                || baseUrl.Scheme is not ("http" or "https"))
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

            result.Add(new BankConnectionSettings(name, SimulatorAdapter, baseUrl, apiKey, timeoutSeconds, pollMinutes));
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
public sealed class ConfiguredBankConnections(IReadOnlyList<BankConnectionSettings> settings) : IBankConnections
{
    public IReadOnlyList<BankConnectionSettings> Settings { get; } = settings;

    public IReadOnlyList<BankConnectionInfo> All { get; } = settings
        .OrderBy(entry => entry.Name, StringComparer.Ordinal)
        .Select(entry => new BankConnectionInfo(entry.Name, entry.Adapter))
        .ToList();

    public bool IsConfigured(string? name) => All.Any(entry => entry.Name == name);
}
