using Microsoft.EntityFrameworkCore;
using MyWealthV2.Simulator.Data;
using MyWealthV2.Simulator.Market;

namespace MyWealthV2.Simulator.Custodian;

/// <summary>The status of one dividend payment of the simulator custodian (dividends R56, R59, R60).</summary>
public enum CustodianDividendPaymentStatus
{
    Due = 0,
    Held = 1,
    Paid = 2
}

/// <summary>
/// What the simulator custodian owes one of its accounts for one dividend (dividends R56): the entitled position when
/// the dividend went ex, the election the platform sent for it, and what it paid. Its own books: it never learns which
/// Account is entitled (R64).
/// </summary>
public sealed class CustodianDividendPayment
{
    private CustodianDividendPayment()
    {
    }

    public int Id { get; private set; }

    public int ActionId { get; private set; }

    public int AccountId { get; private set; }

    public decimal EntitledPosition { get; private set; }

    public string? ElectionInstructionId { get; private set; }

    public string? ElectionBody { get; private set; }

    public decimal? ElectedQuantity { get; private set; }

    public decimal? Shares { get; private set; }

    public decimal? Cash { get; private set; }

    public CustodianDividendPaymentStatus Status { get; private set; }

    public int? BankLineId { get; private set; }

    public int? MovementId { get; private set; }

    public byte[] RowVersion { get; private set; } = null!;

    public static CustodianDividendPayment Due(int actionId, int accountId, decimal entitledPosition) => new()
    {
        ActionId = actionId,
        AccountId = accountId,
        EntitledPosition = entitledPosition,
        Status = CustodianDividendPaymentStatus.Due
    };
}

/// <summary>Dividends R56: a dividend that went ex at the custodian.</summary>
public static class CustodianDividends
{
    /// <summary>One <c>Due</c> payment per account whose position in <paramref name="symbol"/> is above 0, with that
    /// position. No movement: a dividend changes no position.</summary>
    public static async Task GoExAsync(SimulatorDbContext db, MarketCorporateAction action, string symbol, CancellationToken cancellationToken)
    {
        var accounts = await db.CustodianAccounts.AsNoTracking().OrderBy(row => row.Id).ToListAsync(cancellationToken);
        foreach (var account in accounts)
        {
            var position = (await CustodianBook.PositionsAsync(db, account.AccountNumber, null, cancellationToken))
                .Where(row => string.Equals(row.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
                .Sum(row => row.Quantity);
            if (position > 0m)
            {
                db.CustodianDividendPayments.Add(CustodianDividendPayment.Due(action.Id, account.Id, position));
            }
        }
    }
}
