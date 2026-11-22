using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Interest;
using MyWealthV2.Domain.ValueObjects;

namespace MyWealthV2.Application.Interest.Accruals;

/// <summary>
/// Interest-run R16–R22, R28: accrue one account through a day, then post what is due. Reads ignore the tenant filter
/// and name the account's tenant themselves, because a SystemAdmin's manual run and the worker have no current tenant.
/// </summary>
public class AccountInterest(IApplicationDbContext db, ICurrencyCatalog currencies, TimeProvider time) : IAccountInterest
{
    public async Task<AccountInterestResult> AccrueAndPostAsync(
        Account account,
        DateOnly asOfDate,
        DateOnly postingDate,
        Guid runPublicId,
        CancellationToken cancellationToken)
    {
        if (account.CustodyMode != CustodyMode.Custodial)
        {
            return new AccountInterestResult(0, null);
        }

        var added = await AccrueAsync(account, asOfDate, cancellationToken);
        var posting = await PostAsync(account, postingDate, runPublicId, cancellationToken);
        return new AccountInterestResult(added, posting);
    }

    /// <summary>R16–R19: a row for every day after the cursor up to <paramref name="asOfDate"/> that earns above 0.</summary>
    private async Task<int> AccrueAsync(Account account, DateOnly asOfDate, CancellationToken cancellationToken)
    {
        var start = account.InterestAccruedThrough?.AddDays(1) ?? DateOnly.FromDateTime(account.Created.UtcDateTime);
        if (start > asOfDate)
        {
            return 0;
        }

        var currency = account.Currency.Trim();
        var schedules = await db.InterestRateSchedules.IgnoreQueryFilters().AsNoTracking()
            .Include(schedule => schedule.BandRows)
            .Where(schedule => schedule.TenantId == account.TenantId
                               && schedule.Currency == currency
                               && schedule.EffectiveFrom <= asOfDate)
            .OrderBy(schedule => schedule.EffectiveFrom)
            .ToListAsync(cancellationToken);

        var rows = new List<InterestAccrual>();
        if (schedules.Count > 0)
        {
            // R11: a day before the first schedule earns nothing, so the walk starts at the later of the two.
            var first = schedules[0].EffectiveFrom > start ? schedules[0].EffectiveFrom : start;
            var end = StartOf(asOfDate.AddDays(1));
            var legs = await (
                    from leg in db.TransactionCashLegs.IgnoreQueryFilters()
                    join transaction in db.Transactions.IgnoreQueryFilters() on leg.TransactionId equals transaction.Id
                    where leg.AccountId == account.Id && transaction.BookedAt < end
                    orderby transaction.BookedAt
                    select new { transaction.BookedAt, leg.Amount })
                .ToListAsync(cancellationToken);

            var index = 0;
            var balance = 0m;
            for (var day = first; day <= asOfDate; day = day.AddDays(1))
            {
                // R16: the closing balance of the day, every role, booked before the next day starts.
                var next = StartOf(day.AddDays(1));
                while (index < legs.Count && legs[index].BookedAt < next)
                {
                    balance += legs[index].Amount;
                    index++;
                }

                var schedule = schedules.Last(candidate => candidate.EffectiveFrom <= day);
                var accrual = schedule.AccrualFor(balance);
                if (accrual.Amount > 0m)
                {
                    rows.Add(InterestAccrual.Create(
                        account.TenantId, account.Id, day, balance, schedule.Id, accrual.EffectiveRatePercent, accrual.Amount));
                }
            }
        }

        account.MarkInterestAccrued(asOfDate);
        db.InterestAccruals.AddRange(rows);
        await db.SaveChangesAsync(cancellationToken);
        return rows.Count;
    }

    /// <summary>R21–R24: one <c>Interest</c> for every unposted row on or before <paramref name="postingDate"/>.</summary>
    private async Task<Transaction?> PostAsync(
        Account account,
        DateOnly postingDate,
        Guid runPublicId,
        CancellationToken cancellationToken)
    {
        var rows = await db.InterestAccruals.IgnoreQueryFilters()
            .Where(row => row.AccountId == account.Id && row.PostedTransactionId == null && row.Date <= postingDate)
            .OrderBy(row => row.Date)
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return null;
        }

        var currency = account.Currency.Trim();
        var places = currencies.TryGet(currency)?.DecimalPlaces
                     ?? throw new InvalidOperationException($"Currency {currency} is not in the catalog.");
        var accrued = rows.Sum(row => row.Amount);

        // R22: a gross that rounds to 0 posts nothing; the rows count in the next posting.
        if (decimal.Round(accrued, places, MidpointRounding.AwayFromZero) <= 0m)
        {
            return null;
        }

        // R24: the profile when the Transaction is built applies to the whole posting.
        var profile = await db.CustomerTaxProfiles.IgnoreQueryFilters().AsNoTracking()
                          .SingleOrDefaultAsync(row => row.UserId == account.CustomerId, cancellationToken)
                      ?? CustomerTaxProfile.NotProvided;
        var posting = Transaction.PostInterest(
            account.TenantId,
            account.Id,
            currency,
            places,
            accrued,
            new InterestPeriod(rows[0].Date, rows[^1].Date, rows.Count),
            profile.Withholding,
            time.GetUtcNow(),
            TransactionSource.InterestRun(runPublicId));
        posting.CreatedBy = SystemActor.Name;
        foreach (var leg in posting.CashLegs)
        {
            leg.CreatedBy = SystemActor.Name;
        }

        db.Transactions.Add(posting);
        await db.SaveChangesAsync(cancellationToken);
        foreach (var row in rows)
        {
            row.MarkPosted(posting.Id);
        }

        await db.SaveChangesAsync(cancellationToken);
        return posting;
    }

    private static DateTimeOffset StartOf(DateOnly day) => new(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}
