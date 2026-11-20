using Microsoft.EntityFrameworkCore;
using MyWealthV2.Simulator.Data;
using MyWealthV2.Simulator.Worlds;

namespace MyWealthV2.Simulator.Bank;

public sealed record StandingAccount(string AccountNumber, string Currency, string Name);

/// <summary>
/// Deposit matching R39: accounts the simulator opens when the world is created and after every reset, from
/// <c>Simulator:Bank:StandingAccounts</c>. A bad entry stops the host before it listens.
/// </summary>
public sealed class BankStandingAccounts(IReadOnlyList<StandingAccount> accounts)
{
    public const string SectionName = "Simulator:Bank:StandingAccounts";

    public IReadOnlyList<StandingAccount> Accounts { get; } = accounts;

    public static BankStandingAccounts Read(IConfiguration configuration)
    {
        var accounts = new List<StandingAccount>();
        foreach (var entry in configuration.GetSection(SectionName).GetChildren())
        {
            var number = entry["AccountNumber"];
            var currency = entry["Currency"];
            var name = entry["Name"];
            if (!BankRules.IsAccountNumber(number) || !BankRules.IsCurrency(currency) || !BankRules.IsName(name))
            {
                throw new InvalidOperationException(
                    $"{SectionName}:{entry.Key} must have an AccountNumber (1 to 34 letters, digits, spaces and hyphens), " +
                    "a Currency (three upper-case letters) and a Name (1 to 140 characters).");
            }

            var account = new StandingAccount(number!.Trim(), currency!, name!.Trim());
            if (accounts.Any(other => string.Equals(other.AccountNumber, account.AccountNumber, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"{SectionName} lists {account.AccountNumber} twice.");
            }

            accounts.Add(account);
        }

        return new BankStandingAccounts(accounts);
    }

    /// <summary>Adds the standing accounts that are not open; one already open is left as it is. The caller saves.</summary>
    public async Task OpenMissingAsync(SimulatorDbContext db, DateTimeOffset simulatorNow, CancellationToken cancellationToken)
    {
        var open = await db.BankAccounts.Select(account => account.AccountNumber).ToListAsync(cancellationToken);
        foreach (var account in Accounts)
        {
            if (!open.Contains(account.AccountNumber, StringComparer.OrdinalIgnoreCase))
            {
                db.BankAccounts.Add(BankAccount.Open(account.AccountNumber, account.Currency, account.Name, simulatorNow));
            }
        }
    }

    /// <summary>R39 at start-up: the world exists by now.</summary>
    public static async Task EnsureAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SimulatorDbContext>();
        var time = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        var world = await db.Worlds.AsNoTracking().SingleAsync(cancellationToken);
        await scope.ServiceProvider.GetRequiredService<BankStandingAccounts>()
            .OpenMissingAsync(db, world.Now(time.GetUtcNow()), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }
}
