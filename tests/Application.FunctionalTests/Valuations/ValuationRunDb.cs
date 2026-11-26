using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.Valuations;
using MyWealthV2.Application.Valuations.Runs;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.ValueObjects;
using MyWealthV2.Domain.Valuations;
using MyWealthV2.Infrastructure.Accounts;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Valuations;

/// <summary>Direct calls of the valuation run and reads of the stored rows, as the tests of valuation-run §10 need.</summary>
internal static class ValuationRunDb
{
    /// <summary>The last final day of the real clock (R4).</summary>
    public static DateOnly LastFinalDay => ValuationRules.LastFinalDay(ValuationHttp.Today);

    /// <summary>
    /// Makes the account Custodial, with a deposit reference, opened at <paramref name="created"/>. The worked
    /// example's ledger is booked through the API on an External account, because a Custodial <c>bookedAt</c> is
    /// set by the server; the run only sees a Custodial account.
    /// </summary>
    public static async Task MakeCustodialAsync(Guid accountId, DateTimeOffset created)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var reference = DepositReference.Generate(CryptoRandomSymbols.Instance).Value;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE [Accounts] SET [CustodyMode] = 1, [DepositReference] = {reference}, [Created] = {created}
            WHERE [PublicId] = {accountId};
            """);
    }

    public static async Task SetCreatedAsync(Guid accountId, DateOnly day)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var at = new DateTimeOffset(day.ToDateTime(new TimeOnly(1, 0)), TimeSpan.Zero);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [Accounts] SET [Created] = {at} WHERE [PublicId] = {accountId}");
    }

    /// <summary>Moves the completion of every completed closure of the account to <paramref name="day"/> 10:00 UTC.</summary>
    public static async Task SetClosureCompletedAsync(Guid accountId, DateOnly day)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var at = new DateTimeOffset(day.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE [AccountClosures] SET [CompletedAt] = {at}, [RequestedAt] = {at}
            WHERE [Status] = 1 AND [AccountId] = (SELECT [Id] FROM [Accounts] WHERE [PublicId] = {accountId});
            """);
    }

    /// <summary>Calls <see cref="IRunValuations"/> as the daily job (or a manual run) does, through any date.</summary>
    public static async Task<ValuationRun> RunAsync(
        int tenantId,
        DateOnly throughDate,
        ValuationRunTrigger trigger = ValuationRunTrigger.Scheduled,
        int? ranByUserId = null)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IRunValuations>()
            .RunAsync(tenantId, throughDate, trigger, ranByUserId, CancellationToken.None);
    }

    /// <summary>A run whose valuation of the accounts in <paramref name="failing"/> throws (R12).</summary>
    public static async Task<ValuationRun> RunFailingAsync(int tenantId, DateOnly throughDate, params Guid[] failing)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<ApplicationDbContext>();
        var ids = await db.Accounts.IgnoreQueryFilters().Where(row => failing.Contains(row.PublicId)).Select(row => row.Id).ToListAsync();
        var valueAccount = new FailingValueAccount(services.GetRequiredService<IValueAccount>(), ids.ToHashSet());
        var store = ActivatorUtilities.CreateInstance<StoreAccountValuations>(services, (IValueAccount)valueAccount);
        var run = ActivatorUtilities.CreateInstance<RunValuations>(services, (IStoreAccountValuations)store);
        return await run.RunAsync(tenantId, throughDate, ValuationRunTrigger.Scheduled, null, CancellationToken.None);
    }

    public static async Task<List<StoredValuation>> StoredAsync(Guid accountId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var id = await db.Accounts.IgnoreQueryFilters().Where(row => row.PublicId == accountId).Select(row => row.Id).SingleAsync();
        return await db.StoredValuations.IgnoreQueryFilters().AsNoTracking().Include(row => row.Lines)
            .Where(row => row.AccountId == id).OrderBy(row => row.Date).ToListAsync();
    }

    public static async Task<DateOnly?> ValuedThroughAsync(Guid accountId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Accounts.IgnoreQueryFilters().Where(row => row.PublicId == accountId)
            .Select(row => row.ValuedThrough).SingleAsync();
    }

    public static async Task<int> InstrumentIdAsync(Guid instrumentId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Instruments.IgnoreQueryFilters().Where(row => row.PublicId == instrumentId).Select(row => row.Id).SingleAsync();
    }

    public static IEnumerable<DateOnly> Days(DateOnly first, DateOnly last)
    {
        for (var day = first; day <= last; day = day.AddDays(1))
        {
            yield return day;
        }
    }

    private sealed class FailingValueAccount(IValueAccount inner, HashSet<int> failing) : IValueAccount
    {
        public Task<AccountValuationRead> ForDayAsync(Account account, DateOnly day, CancellationToken cancellationToken) =>
            failing.Contains(account.Id)
                ? throw new InvalidOperationException($"Valuation of account {account.Id} failed on purpose.")
                : inner.ForDayAsync(account, day, cancellationToken);
    }
}
