namespace MyWealthV2.Application.Broker;

/// <summary>A configured broker connection as the platform may show it: its name and adapter, never its URL or key
/// (docs/features/orders.md R2, R4).</summary>
public sealed record BrokerConnectionInfo(string Name, string Adapter);

/// <summary>The broker's own bank account that the firm pays settlement cash to, in one currency
/// (docs/features/trade-settlement.md R2). A standing instruction from configuration, never from the feed.</summary>
public sealed record BrokerBankAccount(string Currency, string AccountNumber, string AccountName);

/// <summary>The broker connections configured on this host (orders R2).</summary>
public interface IBrokerConnections
{
    /// <summary>Every configured connection, sorted by name.</summary>
    IReadOnlyList<BrokerConnectionInfo> All { get; }

    /// <summary>True when <paramref name="name"/> is a connection configured on this host.</summary>
    bool IsConfigured(string? name);

    /// <summary>Trade-settlement R2: the broker's bank account of <paramref name="connection"/> in
    /// <paramref name="currency"/>, or <c>null</c> when none is configured.</summary>
    BrokerBankAccount? BankAccount(string? connection, string currency);

    /// <summary>Trade-settlement R2: the currencies <paramref name="connection"/> has a broker bank account in,
    /// sorted.</summary>
    IReadOnlyList<string> SettlementCurrencies(string connection);
}
