using System.Globalization;
using Microsoft.Extensions.Configuration;
using MyWealthV2.Application.Broker;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.ValueObjects;

namespace MyWealthV2.Infrastructure.Broker;

/// <summary>Which host reads the configuration: only <c>worker</c> holds the URL and the secret (orders R2).</summary>
public enum BrokerConnectionHost
{
    WebApi,
    Worker
}

/// <summary>One configured broker connection (orders R2). <see cref="BaseUrl"/> and <see cref="ApiKey"/> are set only
/// on <c>worker</c>; <see cref="BankAccounts"/> (trade-settlement R2) on both hosts, sorted by currency.</summary>
public sealed record BrokerConnectionSettings(
    string Name,
    string Adapter,
    Uri? BaseUrl,
    string? ApiKey,
    int TimeoutSeconds,
    int PollMinutes)
{
    public IReadOnlyList<BrokerBankAccount> BankAccounts { get; init; } = [];
}

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
    public const int AccountNameMaxLength = 140;

    /// <summary>Every value divides an hour, so every run starts on the same minutes of each hour.</summary>
    public static readonly IReadOnlyList<int> AllowedPollMinutes = [1, 2, 3, 4, 5, 6, 10, 12, 15, 20, 30, 60];

    public static IReadOnlyList<BrokerConnectionSettings> Read(IConfiguration configuration, BrokerConnectionHost host)
    {
        var result = new List<BrokerConnectionSettings>();
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

            var bankAccounts = ReadBankAccounts(section, name);
            if (host == BrokerConnectionHost.WebApi)
            {
                result.Add(new BrokerConnectionSettings(name, SimulatorAdapter, null, null, DefaultTimeoutSeconds, DefaultPollMinutes)
                {
                    BankAccounts = bankAccounts
                });
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

            result.Add(new BrokerConnectionSettings(name, SimulatorAdapter, baseUrl, apiKey, timeoutSeconds, pollMinutes)
            {
                BankAccounts = bankAccounts
            });
        }

        return result.OrderBy(entry => entry.Name, StringComparer.Ordinal).ToList();
    }

    /// <summary>Trade-settlement R2: <c>BankAccounts:{currency}:AccountNumber</c> and <c>:AccountName</c>, sorted by
    /// currency.</summary>
    private static IReadOnlyList<BrokerBankAccount> ReadBankAccounts(IConfigurationSection section, string name)
    {
        var accounts = new List<BrokerBankAccount>();
        foreach (var entry in section.GetSection("BankAccounts").GetChildren())
        {
            var currency = entry.Key.ToUpperInvariant();
            if (currency.Length != 3 || !currency.All(ch => ch is >= 'A' and <= 'Z'))
            {
                throw Bad(name, $"BankAccounts:{entry.Key} must be named by a three-letter currency code");
            }

            var number = entry["AccountNumber"];
            if (!ClientMoneyAccount.IsValidAccountNumber(number))
            {
                throw Bad(name, $"BankAccounts:{currency}:AccountNumber must be 1 to {ClientMoneyAccount.AccountNumberMaxLength} letters, digits, spaces and hyphens, with no two spaces in a row");
            }

            var accountName = entry["AccountName"]?.Trim();
            if (string.IsNullOrEmpty(accountName) || accountName.Length > AccountNameMaxLength)
            {
                throw Bad(name, $"BankAccounts:{currency}:AccountName must be 1 to {AccountNameMaxLength} characters");
            }

            accounts.Add(new BrokerBankAccount(currency, number!.Trim(), accountName));
        }

        return accounts.OrderBy(account => account.Currency, StringComparer.Ordinal).ToList();
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
public sealed class ConfiguredBrokerConnections(IReadOnlyList<BrokerConnectionSettings> settings) : IBrokerConnections
{
    public IReadOnlyList<BrokerConnectionSettings> Settings { get; } = settings;

    public IReadOnlyList<BrokerConnectionInfo> All { get; } = settings
        .OrderBy(entry => entry.Name, StringComparer.Ordinal)
        .Select(entry => new BrokerConnectionInfo(entry.Name, entry.Adapter))
        .ToList();

    public bool IsConfigured(string? name) => All.Any(entry => entry.Name == name);

    public BrokerBankAccount? BankAccount(string? connection, string currency) =>
        Settings.FirstOrDefault(entry => entry.Name == connection)?.BankAccounts
            .FirstOrDefault(account => string.Equals(account.Currency, currency.Trim(), StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<string> SettlementCurrencies(string connection) =>
        Settings.FirstOrDefault(entry => entry.Name == connection)?.BankAccounts.Select(account => account.Currency).ToList()
        ?? [];
}
