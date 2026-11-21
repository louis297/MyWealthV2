using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Transactions;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.ValueObjects;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.AccountClosures;

/// <summary>The <c>/accounts/{accountId}/closures</c> routes (docs/features/account-closure.md §8).</summary>
internal static class AccountClosureHttp
{
    public const string Memo = "Customer is moving to another provider";

    public static string Path(Guid accountId) => $"/accounts/{accountId}/closures";

    public static Dictionary<string, object?> Body(Guid? nominatedBankAccountId = null, string? memo = Memo) => new()
    {
        ["nominatedBankAccountId"] = nominatedBankAccountId,
        ["memo"] = memo
    };

    public static Task<HttpResponseMessage> PostAsync(
        string accessToken,
        Guid accountId,
        Dictionary<string, object?> body,
        Guid? key = null) =>
        TransactionHttp.Send(HttpMethod.Post, Path(accountId), accessToken, body, key ?? Guid.NewGuid());

    public static async Task<Guid> RequestAsync(string accessToken, Guid accountId, Guid? nominatedBankAccountId = null)
    {
        var response = await PostAsync(accessToken, accountId, Body(nominatedBankAccountId));
        return await CustodialHttp.ReadIdAsync(response);
    }

    public static Task<HttpResponseMessage> Send(HttpMethod method, string path, string? accessToken, object? body = null) =>
        AccountHttp.Send(method, path, accessToken, body);

    public static async Task<JsonElement> GetAsync(string accessToken, Guid accountId, Guid id)
    {
        var response = await Send(HttpMethod.Get, $"{Path(accountId)}/{id}", accessToken);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.Clone();
    }

    public static async Task<List<Guid>> ListIdsAsync(string accessToken, Guid accountId, string query = "")
    {
        var response = await Send(HttpMethod.Get, Path(accountId) + query, accessToken);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToList();
    }

    public static async Task<JsonElement> AccountAsync(string accessToken, Guid accountId)
    {
        var response = await Send(HttpMethod.Get, $"/accounts/{accountId}", accessToken);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.Clone();
    }

    public static async Task ReopenAsync(string accessToken, Guid accountId)
    {
        var rowVersion = (await AccountAsync(accessToken, accountId)).GetProperty("rowVersion").GetString();
        var response = await Send(HttpMethod.Post, $"/accounts/{accountId}/reopen", accessToken, new { rowVersion });
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
    }

    /// <summary>The three routes, with a body the request accepts.</summary>
    public static IEnumerable<(HttpMethod Method, string Route, object? Body)> Routes(Guid accountId, Guid id)
    {
        yield return (HttpMethod.Get, Path(accountId), null);
        yield return (HttpMethod.Get, $"{Path(accountId)}/{id}", null);
        yield return (HttpMethod.Post, Path(accountId), Body());
    }

    /// <summary>Sends each route; a POST carries a new Idempotency-Key.</summary>
    public static Task<HttpResponseMessage> SendRoute(HttpMethod method, string route, string? accessToken, object? body) =>
        method == HttpMethod.Post
            ? TransactionHttp.Send(method, route, accessToken, body, Guid.NewGuid())
            : Send(method, route, accessToken, body);

    /// <summary>
    /// Cash written straight to the database with source <c>Seed</c>, as a trade's 4-decimal cash leg leaves a
    /// remainder below the minor unit (account-closure R7) without a trade.
    /// </summary>
    public static async Task InsertCashAsync(Guid accountId, decimal amount)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var account = await db.Accounts.IgnoreQueryFilters().SingleAsync(row => row.PublicId == accountId);
        var transaction = Transaction.Post(
            account.TenantId, account.Id, TransactionType.TransferIn, amount, account.Currency, DateTimeOffset.UtcNow,
            memo: null, reference: null, TransactionSource.Seed, currentCashSum: 0m, existingTransactionCount: 1);
        transaction.CreatedBy = "test";
        transaction.CashLegs.ForEach(leg => leg.CreatedBy = "test");
        db.Transactions.Add(transaction);
        await db.SaveChangesAsync();
    }

    public static async Task<List<AccountClosure>> ClosuresAsync(Guid accountId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var account = await db.Accounts.IgnoreQueryFilters().SingleAsync(row => row.PublicId == accountId);
        return await db.AccountClosures.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.AccountId == account.Id).ToListAsync();
    }

    public static async Task<Transaction> TransactionAsync(Guid publicId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Transactions.IgnoreQueryFilters().AsNoTracking().Include(row => row.CashLegs)
            .SingleAsync(row => row.PublicId == publicId);
    }

    public static async Task<List<Withdrawal>> WithdrawalsAsync(Guid accountId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var account = await db.Accounts.IgnoreQueryFilters().SingleAsync(row => row.PublicId == accountId);
        return await db.Withdrawals.IgnoreQueryFilters().AsNoTracking().Where(row => row.AccountId == account.Id).ToListAsync();
    }

    /// <summary>A portfolio created through the API, assigned Active straight in the database (no rebalance).</summary>
    public static async Task InsertActiveAssignmentAsync(string accessToken, Guid accountId)
    {
        var instrument = await CustodialHttp.CreateInstrumentAsync(accessToken, "CLS" + Guid.NewGuid().ToString("N")[..4], "USD", 10m);
        var created = await Send(HttpMethod.Post, "/portfolios", accessToken, new
        {
            name = "Growth",
            baseCurrency = "USD",
            cashResidualPercent = 0m,
            minimumTrade = 1m,
            lines = new[] { new { instrumentId = instrument, targetWeight = 100m } }
        });
        var portfolioId = await CustodialHttp.ReadIdAsync(created);
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var account = await db.Accounts.IgnoreQueryFilters().SingleAsync(row => row.PublicId == accountId);
        var portfolio = await db.Portfolios.IgnoreQueryFilters().SingleAsync(row => row.PublicId == portfolioId);
        var now = DateTimeOffset.UtcNow;
        var assignment = AccountPortfolioAssignment.Assign(null, account.TenantId, account.Id, portfolio.Id, "test", now);
        assignment.Created = now;
        assignment.CreatedBy = "test";
        db.AccountPortfolioAssignments.Add(assignment);
        await db.SaveChangesAsync();
    }
}
