using Microsoft.EntityFrameworkCore;
using MyWealthV2.Simulator.Data;
using MyWealthV2.Simulator.Market;

namespace MyWealthV2.Simulator.Custodian;

/// <summary>In-kind-transfers R68: an effective corporate action changes every custodian position in its symbol.</summary>
public static class CustodianCorporateActions
{
    /// <summary>For every account whose position in <paramref name="symbol"/> is not 0, one <c>CorporateAction</c>
    /// movement for the difference to the old position times the factor, rounded towards zero by the rounding mode.</summary>
    public static async Task ApplyAsync(SimulatorDbContext db, MarketCorporateAction action, string symbol, CancellationToken cancellationToken)
    {
        var settings = await db.CustodianSettings.AsNoTracking().SingleAsync(cancellationToken);
        var world = await db.Worlds.AsNoTracking().SingleAsync(cancellationToken);
        var accounts = await db.CustodianAccounts.AsNoTracking().OrderBy(row => row.Id).ToListAsync(cancellationToken);
        foreach (var account in accounts)
        {
            var old = (await CustodianBook.PositionsAsync(db, account.AccountNumber, null, cancellationToken))
                .Where(position => string.Equals(position.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
                .Sum(position => position.Quantity);
            if (old == 0m)
            {
                continue;
            }

            var exact = old * action.Factor;
            var rounded = settings.RoundingMode == CustodianRoundingMode.WholeShares
                ? decimal.Truncate(exact)
                : decimal.Truncate(exact * 100_000_000m) / 100_000_000m;
            var difference = rounded - old;
            if (difference == 0m)
            {
                continue;
            }

            await CustodianBook.AddAsync(db, account.AccountNumber,
                difference > 0m ? CustodianMovement.InDirection : CustodianMovement.OutDirection, CustodianMovementKind.CorporateAction,
                symbol, account.Currency.Trim(), Math.Abs(difference), action.ExDate, DateTimeOffset.UtcNow,
                corporateActionId: MarketCorporateAction.ActionId(world.WorldId, action.Number), cancellationToken: cancellationToken);
        }
    }
}
