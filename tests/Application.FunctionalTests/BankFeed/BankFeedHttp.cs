using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.BankFeed;
using MyWealthV2.Application.FunctionalTests.ClientMoneyAccounts;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.BankFeed;

/// <summary>Drives the bank statement pull and processing through the functional host's services (deposit-matching §10).</summary>
internal static class BankFeedHttp
{
    public const string Connection = ClientMoneyHttp.Connection;

    public static InMemoryBankFeed Feed => FunctionalTestSetup.WebApiServices.GetRequiredService<InMemoryBankFeed>();

    public static InMemoryBankPayments Payments => FunctionalTestSetup.WebApiServices.GetRequiredService<InMemoryBankPayments>();

    /// <summary>The send step (withdrawals R32).</summary>
    public static async Task SendAsync()
    {
        await using var scope = FunctionalTestSetup.WebApiServices.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SendBankPayments>().RunAsync(Connection);
    }

    public static async Task<BankPullOutcome> PullAsync()
    {
        await using var scope = FunctionalTestSetup.WebApiServices.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PullBankStatements>().RunAsync(Connection);
    }

    public static async Task ProcessAsync()
    {
        await using var scope = FunctionalTestSetup.WebApiServices.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ProcessStatementLines>().RunAsync(Connection);
    }

    /// <summary>Sends, pulls, then processes, as one run of the job does (deposit-matching R13, withdrawals R32).</summary>
    public static async Task<BankPullOutcome> RunAsync()
    {
        await SendAsync();
        var outcome = await PullAsync();
        await ProcessAsync();
        return outcome;
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

    public static async Task<BankStatementLine> LineAsync(string outsideId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.BankStatementLines.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(row => row.Connection == Connection && row.OutsideId == outsideId);
    }

    public static async Task<string> DepositReferenceAsync(Guid accountPublicId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var account = await db.Accounts.IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.PublicId == accountPublicId);
        return account.DepositReference!.Value;
    }

    /// <summary>The Transactions whose source is the statement line, oldest first (R32, R33).</summary>
    public static async Task<List<Transaction>> TransactionsOfLineAsync(Guid linePublicId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Transactions.IgnoreQueryFilters().AsNoTracking()
            .Include(row => row.CashLegs)
            .Include(row => row.BankReceiptDetail)
            .Where(row => row.SourceKind == TransactionSourceKind.BankFeed && row.SourcePublicId == linePublicId)
            .OrderBy(row => row.Id)
            .ToListAsync();
    }

    public static async Task SetCustomerDisabledAsync(int customerId, bool disabled)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var customer = await db.DomainUsers.IgnoreQueryFilters().SingleAsync(row => row.Id == customerId);
        if (disabled)
        {
            customer.Disable();
        }
        else
        {
            customer.Enable();
        }

        await db.SaveChangesAsync();
    }

    public static async Task<BankFeedCursor?> CursorAsync(int clientMoneyAccountId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.BankFeedCursors.AsNoTracking()
            .SingleOrDefaultAsync(row => row.ClientMoneyAccountId == clientMoneyAccountId);
    }
}
