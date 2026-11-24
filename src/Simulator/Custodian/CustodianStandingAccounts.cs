using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MyWealthV2.Simulator.Data;
using MyWealthV2.Simulator.Market;

namespace MyWealthV2.Simulator.Custodian;

public sealed record StandingPosition(string Symbol, decimal Quantity);

public sealed record StandingCustodianAccount(
    string AccountNumber, string Currency, string Name, IReadOnlyList<StandingPosition> Positions, string? IncomeAccountNumber = null);

/// <summary>
/// In-kind-transfers R61: accounts the simulator custodian opens when the world is created and after every reset, from
/// <c>Simulator:Custodian:StandingAccounts</c>, each listed position booked as one movement left out of the feed. A bad
/// entry stops the host before it listens.
/// </summary>
public sealed class CustodianStandingAccounts(IReadOnlyList<StandingCustodianAccount> accounts)
{
    public const string SectionName = "Simulator:Custodian:StandingAccounts";

    public IReadOnlyList<StandingCustodianAccount> Accounts { get; } = accounts;

    public static CustodianStandingAccounts Read(IConfiguration configuration)
    {
        var accounts = new List<StandingCustodianAccount>();
        foreach (var entry in configuration.GetSection(SectionName).GetChildren())
        {
            var number = entry["AccountNumber"];
            var currency = entry["Currency"];
            var name = entry["Name"];
            if (!CustodianRules.IsAccountNumber(number) || !CustodianRules.IsCurrency(currency) || !CustodianRules.IsName(name))
            {
                throw new InvalidOperationException(
                    $"{SectionName}:{entry.Key} must have an AccountNumber (1 to 34 letters, digits, spaces and hyphens), " +
                    "a Currency (three upper-case letters) and a Name (1 to 140 characters).");
            }

            var positions = new List<StandingPosition>();
            foreach (var position in entry.GetSection("Positions").GetChildren())
            {
                var symbol = position["Symbol"];
                if (!CustodianRules.IsSymbol(symbol)
                    || !decimal.TryParse(position["Quantity"], NumberStyles.Number, CultureInfo.InvariantCulture, out var quantity)
                    || !CustodianRules.IsQuantity(quantity))
                {
                    throw new InvalidOperationException(
                        $"{SectionName}:{entry.Key}:Positions:{position.Key} must have a Symbol (1 to 32 characters) and a Quantity above 0 with at most 8 decimal places.");
                }

                positions.Add(new StandingPosition(symbol!.Trim(), quantity));
            }

            // Dividends R57: the bank account its dividends are paid into, optional.
            var income = entry["IncomeAccountNumber"];
            if (!string.IsNullOrWhiteSpace(income) && !CustodianRules.IsAccountNumber(income))
            {
                throw new InvalidOperationException(
                    $"{SectionName}:{entry.Key}:IncomeAccountNumber must be 1 to 34 letters, digits, spaces and hyphens.");
            }

            var account = new StandingCustodianAccount(number!.Trim(), currency!, name!.Trim(), positions,
                string.IsNullOrWhiteSpace(income) ? null : income.Trim());
            if (accounts.Any(other => string.Equals(other.AccountNumber, account.AccountNumber, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"{SectionName} lists {account.AccountNumber} twice.");
            }

            accounts.Add(account);
        }

        return new CustodianStandingAccounts(accounts);
    }

    /// <summary>Opens the standing accounts that are not open, with their positions; one already open is left as it
    /// is. Saves.</summary>
    public async Task OpenMissingAsync(SimulatorDbContext db, DateTimeOffset simulatorNow, CancellationToken cancellationToken)
    {
        var open = await db.CustodianAccounts.Select(account => account.AccountNumber).ToListAsync(cancellationToken);
        foreach (var account in Accounts.Where(account => !open.Contains(account.AccountNumber, StringComparer.OrdinalIgnoreCase)))
        {
            db.CustodianAccounts.Add(CustodianAccount.Open(
                account.AccountNumber, account.Currency, account.Name, simulatorNow, account.IncomeAccountNumber));
            await db.SaveChangesAsync(cancellationToken);
            foreach (var position in account.Positions)
            {
                await CustodianBook.AddAsync(db, account.AccountNumber, CustodianMovement.InDirection, CustodianMovementKind.Transfer,
                    position.Symbol, account.Currency, position.Quantity, MarketPricePath.Day(simulatorNow), simulatorNow,
                    reference: "Standing position", omitFromFeed: true, cancellationToken: cancellationToken);
            }
        }
    }

    /// <summary>R61 at start-up: the world exists by now.</summary>
    public static async Task EnsureAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SimulatorDbContext>();
        var time = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        var world = await db.Worlds.AsNoTracking().SingleAsync(cancellationToken);
        await scope.ServiceProvider.GetRequiredService<CustodianStandingAccounts>()
            .OpenMissingAsync(db, world.Now(time.GetUtcNow()), cancellationToken);
    }
}
