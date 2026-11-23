using MyWealthV2.Application.Broker;

namespace MyWealthV2.Application.FunctionalTests.Broker;

/// <summary>The functional host's broker connections: one connection, <c>simulator</c> (orders §9.1), with no broker
/// bank account until a test sets one (trade-settlement §9.1). Reset before each test.</summary>
internal sealed class FakeBrokerConnections : IBrokerConnections
{
    public const string Simulator = "simulator";

    private readonly List<BrokerBankAccount> _bankAccounts = [];

    public IReadOnlyList<BrokerConnectionInfo> All { get; } = [new(Simulator, "Simulator")];

    public bool IsConfigured(string? name) => All.Any(entry => entry.Name == name);

    public BrokerBankAccount? BankAccount(string? connection, string currency)
    {
        lock (_bankAccounts)
        {
            return connection == Simulator
                ? _bankAccounts.FirstOrDefault(account => account.Currency == currency)
                : null;
        }
    }

    public IReadOnlyList<string> SettlementCurrencies(string connection)
    {
        lock (_bankAccounts)
        {
            return connection == Simulator
                ? _bankAccounts.Select(account => account.Currency).Order(StringComparer.Ordinal).ToList()
                : [];
        }
    }

    /// <summary>Configures the broker's bank account of <c>simulator</c> in <paramref name="currency"/>.</summary>
    public void SetBankAccount(string currency, string accountNumber = "BRK-SIM-", string accountName = "Simulator Broker")
    {
        lock (_bankAccounts)
        {
            _bankAccounts.RemoveAll(account => account.Currency == currency);
            _bankAccounts.Add(new BrokerBankAccount(
                currency, accountNumber == "BRK-SIM-" ? accountNumber + currency : accountNumber, accountName));
        }
    }

    public void Reset()
    {
        lock (_bankAccounts)
        {
            _bankAccounts.Clear();
        }
    }
}
