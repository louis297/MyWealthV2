using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.Interest.Runs;

/// <summary>Interest-run R37: maps runs to items, with the totals summed from their Transactions.</summary>
internal static class InterestRunReader
{
    public static async Task<List<InterestRunDto>> ItemsAsync(
        IApplicationDbContext db,
        IReadOnlyList<InterestRun> runs,
        CancellationToken cancellationToken)
    {
        var publicIds = runs.Select(run => (Guid?)run.PublicId).ToList();
        var legs = await (
                from leg in db.TransactionCashLegs.IgnoreQueryFilters()
                join transaction in db.Transactions.IgnoreQueryFilters() on leg.TransactionId equals transaction.Id
                where transaction.SourceKind == TransactionSourceKind.InterestRun
                      && publicIds.Contains(transaction.SourcePublicId)
                      && transaction.Type == TransactionType.Interest
                select new { RunId = transaction.SourcePublicId!.Value, transaction.Id, leg.Role, leg.Amount, leg.Currency })
            .ToListAsync(cancellationToken);
        var personIds = runs.Select(run => run.RanByUserId).OfType<int>().Distinct().ToList();
        var people = await db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(user => personIds.Contains(user.Id))
            .Select(user => new { user.Id, user.PublicId, user.Name })
            .ToDictionaryAsync(user => user.Id, cancellationToken);
        var accountIds = runs.Select(run => run.AccountId).OfType<int>().Distinct().ToList();
        var accounts = await db.Accounts.IgnoreQueryFilters().AsNoTracking()
            .Where(account => accountIds.Contains(account.Id))
            .ToDictionaryAsync(account => account.Id, account => account.PublicId, cancellationToken);

        return runs.Select(run =>
        {
            var totals = legs.Where(leg => leg.RunId == run.PublicId)
                .GroupBy(leg => leg.Currency.Trim())
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new InterestRunTotalDto(
                    group.Key,
                    group.Select(leg => leg.Id).Distinct().Count(),
                    group.Where(leg => leg.Role == CashLegRole.Principal).Sum(leg => leg.Amount),
                    -group.Where(leg => leg.Role == CashLegRole.Tax).Sum(leg => leg.Amount)))
                .ToList();
            return new InterestRunDto(
                run.PublicId,
                run.AsOfDate,
                run.PostingDate,
                run.Trigger.ToString(),
                run.RanByUserId is { } person && people.TryGetValue(person, out var who)
                    ? new InterestRunPersonDto(who.PublicId, who.Name)
                    : null,
                run.AccountId is { } account && accounts.TryGetValue(account, out var accountPublicId) ? accountPublicId : null,
                run.StartedAt,
                run.CompletedAt,
                run.AccountsProcessed,
                run.AccountsFailed,
                run.PostingCount,
                totals);
        }).ToList();
    }
}
