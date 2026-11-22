using System.Globalization;
using Microsoft.Extensions.Configuration;
using MyWealthV2.Domain.MarketData;
using MyWealthV2.Domain.ValueObjects;

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

    /// <summary>Every value divides an hour, so every run starts on the same minutes of each hour.</summary>
    public static readonly IReadOnlyList<int> AllowedPollMinutes = [1, 2, 3, 4, 5, 6, 10, 12, 15, 20, 30, 60];

    public static IReadOnlyList<MarketDataConnectionSettings> Read(IConfiguration configuration, MarketDataHost host)
    {
        var result = new List<MarketDataConnectionSettings>();
        foreach (var section in configuration.GetSection(SectionName).GetChildren())
        {
            var name = section.Key;
            // R1: the same name rule as a bank connection.
            if (!BankConnectionName.IsValid(name))
            {
                throw Bad(name, "the name must be 1 to 50 characters: a lower-case letter, then lower-case letters, digits and hyphens");
            }

            var adapter = section["Adapter"];
            if (!string.Equals(adapter, SimulatorAdapter, StringComparison.Ordinal))
            {
                throw Bad(name, $"Adapter must be {SimulatorAdapter}");
            }

            var suffix = section["SymbolSuffix"];
            if (!MarketDataConnection.IsValidSuffix(suffix))
            {
                throw Bad(name, "SymbolSuffix must be a . followed by 2 to 10 letters or digits");
            }

            // R2: no two connections serve the same symbols.
            var sameSuffix = result.FirstOrDefault(entry =>
                string.Equals(entry.SymbolSuffix, suffix, StringComparison.OrdinalIgnoreCase));
            if (sameSuffix is not null)
            {
                throw Bad(name, $"SymbolSuffix {suffix} is already used by {sameSuffix.Name}");
            }

            if (host == MarketDataHost.WebApi)
            {
                result.Add(new MarketDataConnectionSettings(name, SimulatorAdapter, suffix!, null, null, DefaultTimeoutSeconds, DefaultPollMinutes));
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

            result.Add(new MarketDataConnectionSettings(name, SimulatorAdapter, suffix!, baseUrl, apiKey, timeoutSeconds, pollMinutes));
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

/// <summary>The configured connections: the full settings for the adapters and jobs, and the domain view.</summary>
public sealed class ConfiguredMarketDataConnections(IReadOnlyList<MarketDataConnectionSettings> settings)
{
    public IReadOnlyList<MarketDataConnectionSettings> Settings { get; } = settings
        .OrderBy(entry => entry.Name, StringComparer.Ordinal)
        .ToList();

    public MarketDataConnections Connections { get; } = new(settings.Select(entry => entry.Connection));
}
