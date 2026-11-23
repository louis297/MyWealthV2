using MyWealthV2.Simulator.Bank;

namespace MyWealthV2.Simulator.Broker;

/// <summary>The broker's own bank account in one currency (trade-settlement R37): the payer of its settlement
/// receipts.</summary>
public sealed record BrokerBankAccount(string Currency, string AccountNumber, string Name);

/// <summary>
/// Trade-settlement R37: <c>Simulator:Broker:BankAccounts</c> lists the broker's own bank accounts. A bad entry stops
/// the host before it listens.
/// </summary>
public sealed class BrokerBankAccounts(IReadOnlyList<BrokerBankAccount> accounts)
{
    public const string SectionName = "Simulator:Broker:BankAccounts";

    /// <summary>R37: the payer name of a receipt when the broker has no account in the currency.</summary>
    public const string DefaultPayerName = "Simulator Broker";

    public IReadOnlyList<BrokerBankAccount> Accounts { get; } = accounts;

    public BrokerBankAccount? In(string currency) =>
        Accounts.FirstOrDefault(account => string.Equals(account.Currency, currency.Trim(), StringComparison.Ordinal));

    public static BrokerBankAccounts Read(IConfiguration configuration)
    {
        var accounts = new List<BrokerBankAccount>();
        foreach (var entry in configuration.GetSection(SectionName).GetChildren())
        {
            var currency = entry["Currency"];
            var number = entry["AccountNumber"];
            var name = entry["Name"];
            if (!BankRules.IsCurrency(currency) || !BankRules.IsAccountNumber(number) || !BankRules.IsName(name))
            {
                throw new InvalidOperationException(
                    $"{SectionName}:{entry.Key} must have a Currency (three upper-case letters), an AccountNumber (1 to 34 " +
                    "letters, digits, spaces and hyphens) and a Name (1 to 140 characters).");
            }

            accounts.Add(new BrokerBankAccount(currency!, number!.Trim(), name!.Trim()));
        }

        return new BrokerBankAccounts(accounts);
    }
}
