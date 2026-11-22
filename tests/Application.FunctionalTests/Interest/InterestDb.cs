using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.Interest.Accruals;
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

    /// <summary>R17: <c>Round(balance × rate / 100 / 365, 8)</c> for one band.</summary>
    public static decimal Daily(decimal balance, decimal ratePercent) =>
        decimal.Round(balance * ratePercent / 100m / 365m, 8, MidpointRounding.AwayFromZero);

    public static decimal Round2(decimal amount) => decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
}
