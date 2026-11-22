using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.Interest.Accruals;
using MyWealthV2.Application.Interest.Runs;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Interest;

/// <summary>Direct calls of the interest service and reads of the interest rows, as the tests of interest-run §10 need.</summary>
internal static class InterestDb
{
    /// <summary>The first day of next month (UTC): a schedule date after today's cut-off, with a whole month after it.</summary>
    public static DateOnly NextMonth
    {
        get
        {
            var today = InterestHttp.Today;
            return new DateOnly(today.Year, today.Month, 1).AddMonths(1);
        }
    }

    public static DateOnly MonthEnd(DateOnly day) => new DateOnly(day.Year, day.Month, 1).AddMonths(1).AddDays(-1);

    /// <summary>Calls <see cref="IAccountInterest"/> for one account as a run does: in one database transaction under
    /// the Account lock.</summary>
    public static async Task<(int RowsAdded, Guid? PostingId)> AccrueAndPostAsync(
        Guid accountId,
        DateOnly asOfDate,
        DateOnly postingDate,
        Guid? runId = null)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<IAccountInterest>();
        AccountInterestResult? result = null;
        await db.ExecuteInTransactionAsync(async token =>
        {
            var id = await db.Accounts.IgnoreQueryFilters().Where(row => row.PublicId == accountId).Select(row => row.Id).SingleAsync(token);
            await db.LockAccountAsync(id, token);
            var account = await db.Accounts.IgnoreQueryFilters().SingleAsync(row => row.Id == id, token);
            result = await service.AccrueAndPostAsync(account, asOfDate, postingDate, runId ?? Guid.NewGuid(), token);
        }, CancellationToken.None);
        return (result!.RowsAdded, result.Posting?.PublicId);
    }

    public static async Task<List<InterestAccrual>> RowsAsync(Guid accountId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var id = await db.Accounts.IgnoreQueryFilters().Where(row => row.PublicId == accountId).Select(row => row.Id).SingleAsync();
        return await db.InterestAccruals.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.AccountId == id).OrderBy(row => row.Date).ToListAsync();
    }

    public static async Task<DateOnly?> AccruedThroughAsync(Guid accountId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Accounts.IgnoreQueryFilters().Where(row => row.PublicId == accountId)
            .Select(row => row.InterestAccruedThrough).SingleAsync();
    }

    /// <summary>The account's interest postings with their legs and details.</summary>
    public static async Task<List<Transaction>> PostingsAsync(Guid accountId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var id = await db.Accounts.IgnoreQueryFilters().Where(row => row.PublicId == accountId).Select(row => row.Id).SingleAsync();
        return await db.Transactions.IgnoreQueryFilters().AsNoTracking()
            .Include(row => row.CashLegs).ThenInclude(leg => leg.TaxDetail)
            .Include(row => row.InterestDetail)
            .Where(row => row.Type == TransactionType.Interest && row.CashLegs.Any(leg => leg.AccountId == id))
            .OrderBy(row => row.Id)
            .ToListAsync();
    }

    public static async Task<int> ScheduleInternalIdAsync(Guid scheduleId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.InterestRateSchedules.IgnoreQueryFilters().Where(row => row.PublicId == scheduleId).Select(row => row.Id).SingleAsync();
    }

    /// <summary>A schedule straight in the database from a past day, which the routes cannot create (R13).</summary>
    public static async Task InsertScheduleAsync(int tenantId, string currency, DateOnly effectiveFrom, decimal ratePercent = 3m)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var catalog = await db.Set<Currency>().AsNoTracking().SingleAsync(row => row.Code == currency);
        var schedule = InterestRateSchedule.Create(
            tenantId, catalog, effectiveFrom, [new Domain.ValueObjects.FeeBand(0m, ratePercent)], effectiveFrom.AddDays(-1));
        schedule.CreatedBy = "test";
        db.InterestRateSchedules.Add(schedule);
        await db.SaveChangesAsync();
    }

    /// <summary>Moves the account's open date and every Transaction on it back to <paramref name="day"/>, as if it had
    /// been funded then (a Custodial account cannot be dated by a caller).</summary>
    public static async Task BackdateAsync(Guid accountId, DateOnly day)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var at = new DateTimeOffset(day.ToDateTime(new TimeOnly(1, 0)), TimeSpan.Zero);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE [Accounts] SET [Created] = {at} WHERE [PublicId] = {accountId};
            UPDATE [Transactions] SET [BookedAt] = {at}
            WHERE [Id] IN (SELECT leg.[TransactionId] FROM [TransactionCashLegs] leg
                           JOIN [Accounts] account ON account.[Id] = leg.[AccountId] WHERE account.[PublicId] = {accountId});
            """);
    }

    /// <summary>Calls <see cref="IRunInterest"/> as the daily job does, with any as-of date.</summary>
    public static async Task RunAsync(int tenantId, DateOnly asOfDate)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IRunInterest>()
            .RunAsync(tenantId, asOfDate, InterestRunTrigger.Scheduled, null, CancellationToken.None);
    }

    public static async Task<List<InterestRun>> RunsAsync(int tenantId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.InterestRuns.IgnoreQueryFilters().AsNoTracking().Where(row => row.TenantId == tenantId).ToListAsync();
    }

    /// <summary>R17: <c>Round(balance × rate / 100 / 365, 8)</c> for one band.</summary>
    public static decimal Daily(decimal balance, decimal ratePercent) =>
        decimal.Round(balance * ratePercent / 100m / 365m, 8, MidpointRounding.AwayFromZero);

    public static decimal Round2(decimal amount) => decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
}
