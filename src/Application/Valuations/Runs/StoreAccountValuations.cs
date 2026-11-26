using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Valuations;

namespace MyWealthV2.Application.Valuations.Runs;

/// <summary>
/// Valuation-run R3, R5, R6, R8–R12: one Custodial account through a date. Each day after the valued-through date
/// (or from the day the account was opened) up to the earlier of the through date and the account's last day is
/// valued by <see cref="IValueAccount"/> and stored in its own database transaction under the Account lock, which
/// also reads the valued-through date again, so a day is never stored twice. A failure rolls that day back and
/// stops the account there.
/// </summary>
public class StoreAccountValuations(IApplicationDbContext db, IValueAccount valueAccount, TimeProvider time) : IStoreAccountValuations
{
    public async Task<StoredDays> ValueThroughAsync(
        Account account, DateOnly throughDate, ValuationRun run, CancellationToken cancellationToken)
    {
        if (account.CustodyMode != CustodyMode.Custodial)
        {
            return StoredDays.None;
        }

        var accountId = account.Id;
        var firstDay = ValuationDays.FirstDay(account.Created);
        var lastDay = await LastDayAsync(account, throughDate, cancellationToken);
        var next = account.ValuedThrough?.AddDays(1) ?? firstDay;
        if (next > lastDay)
        {
            return StoredDays.None;
        }

        var valued = 0;
        var incomplete = 0;
        for (var day = next; day <= lastDay; day = day.AddDays(1))
        {
            db.ClearTrackedChanges();
            var stored = false;
            var complete = true;
            try
            {
                await db.ExecuteInTransactionAsync(async token =>
                {
                    stored = false;
                    await db.LockAccountAsync(accountId, token);
                    var locked = await db.Accounts.IgnoreQueryFilters()
                        .SingleAsync(row => row.Id == accountId && row.TenantId == account.TenantId, token);

                    // R6: only the day after the valued-through date; a day already stored is left as it is.
                    if ((locked.ValuedThrough?.AddDays(1) ?? firstDay) != day)
                    {
                        return;
                    }

                    var read = await valueAccount.ForDayAsync(locked, day, token);
                    var row = StoredValuation.From(locked.TenantId, locked.Id, read.Valuation, run.Id, time.GetUtcNow());
                    db.StoredValuations.Add(row);
                    locked.MarkValued(day);
                    await db.SaveChangesAsync(token);
                    stored = true;
                    complete = row.Complete;
                }, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                db.ClearTrackedChanges();
                return new StoredDays(true, valued, incomplete, day, exception);
            }

            if (stored)
            {
                valued++;
                if (!complete)
                {
                    incomplete++;
                }
            }
        }

        db.ClearTrackedChanges();
        return new StoredDays(true, valued, incomplete);
    }

    /// <summary>R3: the through date, or for a Closed account the earlier of it and the UTC day its latest completed
    /// closure completed.</summary>
    private async Task<DateOnly> LastDayAsync(Account account, DateOnly throughDate, CancellationToken cancellationToken)
    {
        if (account.Status != AccountStatus.Closed)
        {
            return throughDate;
        }

        var closures = await ValuationDays.LastClosuresAsync(db, account.TenantId, [account.Id], cancellationToken);
        var lastDay = ValuationDays.LastDay(account.Status, closures.GetValueOrDefault(account.Id));
        return lastDay is { } last && last < throughDate ? last : throughDate;
    }
}
