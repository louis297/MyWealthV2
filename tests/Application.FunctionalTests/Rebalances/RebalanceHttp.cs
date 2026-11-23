using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Orders;
using MyWealthV2.Application.FunctionalTests.Portfolios;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Data.Entities;

namespace MyWealthV2.Application.FunctionalTests.Rebalances;

/// <summary>Calls and reads of the rebalance routes (docs/features/rebalances.md §8).</summary>
internal static class RebalanceHttp
{
    public static Task<HttpResponseMessage> Send(HttpMethod method, string path, string? accessToken, object? body = null) =>
        PortfolioHttp.Send(method, path, accessToken, body);

    /// <summary><c>POST /accounts/{id}/rebalance</c>.</summary>
    public static Task<HttpResponseMessage> ProposeAsync(string accessToken, Guid accountId) =>
        Send(HttpMethod.Post, $"/accounts/{accountId}/rebalance", accessToken);

    /// <summary>Proposes and returns <c>(status, rebalanceId)</c> of a 200.</summary>
    public static async Task<(string Status, Guid RebalanceId)> ProposedAsync(string accessToken, Guid accountId)
    {
        var response = await ProposeAsync(accessToken, accountId);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (json.RootElement.GetProperty("status").GetString()!, json.RootElement.GetProperty("rebalanceId").GetGuid());
    }

    public static async Task<JsonElement> GetAsync(string accessToken, Guid id)
    {
        var response = await Send(HttpMethod.Get, $"/rebalances/{id}", accessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

    public static async Task<List<JsonElement>> ListAsync(string accessToken, string query = "")
    {
        var response = await Send(HttpMethod.Get, $"/rebalances{query}", accessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("items").EnumerateArray().Select(item => item.Clone()).ToList();
    }

    public static async Task<List<Guid>> ListIdsAsync(string accessToken, string query = "") =>
        (await ListAsync(accessToken, query)).Select(item => item.GetProperty("id").GetGuid()).ToList();

    /// <summary>The problem <c>code</c> of a 400.</summary>
    public static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    /// <summary>The problem <c>detail</c> of a response.</summary>
    public static async Task<string?> DetailAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.TryGetProperty("detail", out var detail) ? detail.GetString() : null;
    }

    /// <summary>Creates a portfolio with the base currency and the lines given, as a TenantAdmin.</summary>
    public static async Task<Guid> CreatePortfolioAsync(
        string accessToken, string baseCurrency, params (Guid InstrumentId, decimal Weight)[] lines)
    {
        var response = await PortfolioHttp.Send(HttpMethod.Post, "/portfolios", accessToken, new
        {
            name = "Demo Growth",
            baseCurrency,
            lines = lines.Select(line => new { instrumentId = line.InstrumentId, targetWeight = line.Weight }).ToArray()
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await CustodialHttp.ReadIdAsync(response);
    }

    /// <summary>An Active assignment written straight to the database, so no rebalance is proposed.</summary>
    public static async Task InsertAssignmentAsync(Guid accountPublicId, Guid portfolioPublicId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var account = await db.Accounts.IgnoreQueryFilters().SingleAsync(row => row.PublicId == accountPublicId);
        var portfolio = await db.Portfolios.IgnoreQueryFilters().SingleAsync(row => row.PublicId == portfolioPublicId);
        var now = DateTimeOffset.UtcNow;
        var assignment = AccountPortfolioAssignment.Assign(null, account.TenantId, account.Id, portfolio.Id, "test", now);
        assignment.Created = now;
        assignment.CreatedBy = "test";
        db.AccountPortfolioAssignments.Add(assignment);
        await db.SaveChangesAsync();
    }

    public static async Task<Rebalance> RebalanceAsync(Guid id)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Rebalances.IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.PublicId == id);
    }

    public static async Task<List<Rebalance>> RebalancesOfAsync(Guid accountPublicId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var account = await db.Accounts.IgnoreQueryFilters().SingleAsync(row => row.PublicId == accountPublicId);
        return await db.Rebalances.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.AccountId == account.Id)
            .OrderBy(row => row.Id)
            .ToListAsync();
    }

    /// <summary>Sets <c>IsActive = false</c> on an instrument, as disabling it does.</summary>
    public static async Task DisableInstrumentAsync(Guid instrumentPublicId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Instruments.IgnoreQueryFilters()
            .Where(row => row.PublicId == instrumentPublicId)
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.IsActive, false));
    }

    /// <summary>Stores an FX rate <paramref name="from"/>/<paramref name="to"/>.</summary>
    public static async Task SetRateAsync(string from, string to, decimal rate)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.FxRates.Add(new FxRate
        {
            BaseCurrency = from,
            QuoteCurrency = to,
            Rate = rate,
            AsOfDate = OrderHttp.Today,
            Source = "Frankfurter",
            FetchedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }
}

/// <summary>
/// The rebalance test world (rebalances §8.1): the orders world (a USD Custodial account of Ned, funded with
/// 10 000.00, <c>ACME</c> at 100.00, a USD fee schedule of 0.5 % with a minimum of 5.00) with <c>BETA</c> at 50.00
/// and the portfolio "Demo Growth" in USD, 60 % ACME and 40 % BETA, minimum trade 100.00 and a residual of 1 %,
/// assigned to the account without a proposal.
/// </summary>
internal sealed record RebalanceWorld(OrderWorld Orders, Guid BetaId, Guid PortfolioId)
{
    public Guid AccountId => Orders.AccountId;
    public Guid AcmeId => Orders.InstrumentId;
    public string TenantAdmin => Orders.TenantAdmin;
    public string Adviser => Orders.Adviser;
    public string CustomerToken => Orders.CustomerToken;
    public Tenant Tenant => Orders.Tenant;

    public static async Task<RebalanceWorld> CreateAsync(
        string name = "North Advisory",
        string code = "north-advisory",
        decimal funding = 10000m,
        DateOnly? betaPriceDate = null,
        bool assign = true)
    {
        var orders = await OrderWorld.CreateAsync(name, code, funding);
        var beta = await OrderHttp.CreateInstrumentAsync(orders.TenantAdmin, "BETA", "USD", 50m, betaPriceDate);
        var portfolio = await RebalanceHttp.CreatePortfolioAsync(orders.TenantAdmin, "USD", (orders.InstrumentId, 60m), (beta, 40m));
        if (assign)
        {
            await RebalanceHttp.InsertAssignmentAsync(orders.AccountId, portfolio);
        }

        return new RebalanceWorld(orders, beta, portfolio);
    }

    public Task<(string Status, Guid RebalanceId)> ProposeAsync() => RebalanceHttp.ProposedAsync(Adviser, AccountId);
}
