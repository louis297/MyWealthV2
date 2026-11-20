using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.BankFeed;
using MyWealthV2.Application.FunctionalTests.ClientMoneyAccounts;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.BankFeed;

/// <summary>Drives the bank statement pull and processing through the functional host's services (deposit-matching §10).</summary>
internal static class BankFeedHttp
{
    public const string Connection = ClientMoneyHttp.Connection;

    public static InMemoryBankFeed Feed => FunctionalTestSetup.WebApiServices.GetRequiredService<InMemoryBankFeed>();

    public static async Task<BankPullOutcome> PullAsync()
    {
        await using var scope = FunctionalTestSetup.WebApiServices.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PullBankStatements>().RunAsync(Connection);
    }

    /// <summary>The tenant's active client money account in <paramref name="currency"/>, created when missing.</summary>
    public static async Task<ClientMoneyAccount> ClientMoneyAccountAsync(int tenantId, string currency = "USD")
    {
        await ClientMoneyHttp.EnsureActiveAsync(tenantId, currency);
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.ClientMoneyAccounts.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(row => row.TenantId == tenantId && row.Currency == currency && row.IsActive);
    }

    public static async Task<List<BankStatementLine>> LinesAsync(int clientMoneyAccountId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.BankStatementLines.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.ClientMoneyAccountId == clientMoneyAccountId)
            .OrderBy(row => row.Id)
            .ToListAsync();
    }

    public static async Task<List<BankStatementLine>> AllLinesAsync()
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.BankStatementLines.IgnoreQueryFilters().AsNoTracking().OrderBy(row => row.Id).ToListAsync();
    }

    public static async Task<BankFeedCursor?> CursorAsync(int clientMoneyAccountId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.BankFeedCursors.AsNoTracking()
            .SingleOrDefaultAsync(row => row.ClientMoneyAccountId == clientMoneyAccountId);
    }
}
