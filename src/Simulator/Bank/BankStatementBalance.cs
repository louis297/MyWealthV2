using Microsoft.EntityFrameworkCore;
using MyWealthV2.Simulator.Data;

namespace MyWealthV2.Simulator.Bank;

/// <summary>
/// Bank-reconciliation R26, R29: the bank's balance at a place of an account's feed: the signed sum of the lines with a
/// place at or before it, each line counted once (a line delivered twice has two places), lines left out of the feed
/// included. Before the first place it is <c>0</c>.
/// </summary>
internal static class BankStatementBalance
{
    public static async Task<decimal> AtAsync(SimulatorDbContext db, int accountId, long place, CancellationToken cancellationToken) =>
        await db.BankLines.AsNoTracking()
            .Where(line => line.BankAccountId == accountId
                           && db.BankFeedPlaces.Any(feed => feed.BankLineId == line.Id && feed.Place <= place))
            .SumAsync(line => (decimal?)(line.Direction == BankLine.CreditDirection ? line.Amount : -line.Amount), cancellationToken)
        ?? 0m;

    /// <summary>The balance at each account's last place: every line with a place.</summary>
    public static async Task<Dictionary<int, decimal>> AtLastPlaceAsync(SimulatorDbContext db, CancellationToken cancellationToken) =>
        await db.BankLines.AsNoTracking()
            .Where(line => db.BankFeedPlaces.Any(feed => feed.BankLineId == line.Id))
            .GroupBy(line => line.BankAccountId)
            .Select(group => new
            {
                group.Key,
                Balance = group.Sum(line => line.Direction == BankLine.CreditDirection ? line.Amount : -line.Amount)
            })
            .ToDictionaryAsync(row => row.Key, row => row.Balance, cancellationToken);
}
