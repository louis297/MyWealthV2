using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Transactions;
using MyWealthV2.Application.FunctionalTests.Withdrawals;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.AdjustmentRequests;

/// <summary>The <c>/adjustment-requests</c> routes (docs/features/adjustment-review.md §8).</summary>
internal static class AdjustmentRequestHttp
{
    public const string Path = "/adjustment-requests";
    public const string StaleRowVersion = "AAAAAAAAAAE=";

    public static Task<HttpResponseMessage> Send(HttpMethod method, string path, string? accessToken, object? body = null) =>
        AccountHttp.Send(method, path, accessToken, body);

    public static Task<HttpResponseMessage> PostAsync(string accessToken, Dictionary<string, object?> body, Guid? key = null) =>
        TransactionHttp.Send(HttpMethod.Post, Path, accessToken, body, key ?? Guid.NewGuid());

    public static async Task<Guid> RequestAsync(string accessToken, Dictionary<string, object?> body)
    {
        var response = await PostAsync(accessToken, body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await CustodialHttp.ReadIdAsync(response);
    }

    public static async Task<JsonElement> GetAsync(string accessToken, Guid id)
    {
        var response = await Send(HttpMethod.Get, $"{Path}/{id}", accessToken);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.Clone();
    }

    public static async Task<List<Guid>> ListIdsAsync(string accessToken, string query = "")
    {
        var response = await Send(HttpMethod.Get, Path + query, accessToken);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToList();
    }

    /// <summary>Runs an action with the request's current <c>rowVersion</c>, read with <paramref name="readToken"/>.</summary>
    public static async Task<HttpResponseMessage> ActAsync(
        string accessToken, Guid id, string action, string? note = null, string? readToken = null)
    {
        var rowVersion = (await GetAsync(readToken ?? accessToken, id)).GetProperty("rowVersion").GetString();
        var body = new Dictionary<string, object?> { ["rowVersion"] = rowVersion };
        if (note is not null)
        {
            body["note"] = note;
        }

        return await Send(HttpMethod.Post, $"{Path}/{id}/{action}", accessToken, body);
    }

    public static async Task<int> CountAsync(Guid accountId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var account = await db.Accounts.IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.PublicId == accountId);
        return await db.AdjustmentRequests.IgnoreQueryFilters().CountAsync(row => row.AccountId == account.Id);
    }

    public static async Task<AdjustmentRequest> RowAsync(Guid id)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.AdjustmentRequests.IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.PublicId == id);
    }

    public static async Task<User> PersonAsync(string email)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.DomainUsers.IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.Email == email);
    }
}

/// <summary>
/// One tenant for the adjustment review tests: two TenantAdmins (Tess requests, Tia approves), an Adviser and their
/// Customer, a Custodial USD account funded with 1 000.00 and a nominated bank account, an External account and one
/// USD instrument priced at 25.
/// </summary>
internal sealed record AdjustmentRequestWorld(
    WithdrawalWorld Withdrawals,
    User Tess,
    User Tia,
    Guid ExternalAccountId,
    Guid InstrumentId)
{
    public Tenant Tenant => Withdrawals.Tenant;
    public User Customer => Withdrawals.Customer;
    public Guid AccountId => Withdrawals.AccountId;
    public string TenantAdmin => Withdrawals.TenantAdmin;
    public string SecondTenantAdmin => Withdrawals.SecondTenantAdmin;
    public string Adviser => Withdrawals.Adviser;
    public string CustomerToken => Withdrawals.CustomerToken;

    public static async Task<AdjustmentRequestWorld> CreateAsync(string name = "North Advisory", string code = "north-advisory")
    {
        var withdrawals = await WithdrawalWorld.CreateAsync(name, code);
        var external = await CustodialHttp.OpenExternalAsync(withdrawals.TenantAdmin, withdrawals.Customer.PublicId, "Held elsewhere");
        var instrument = await CustodialHttp.CreateInstrumentAsync(withdrawals.TenantAdmin, "VTI", "USD", 25m);
        return new AdjustmentRequestWorld(
            withdrawals,
            await AdjustmentRequestHttp.PersonAsync($"tess@{code}.example"),
            await AdjustmentRequestHttp.PersonAsync($"tia@{code}.example"),
            external,
            instrument);
    }

    public Dictionary<string, object?> Body(decimal? amount = null, object? security = null, string reason = "EntryError") =>
        AdjustmentWorld.Body(AccountId, amount, security, reason);

    public object Holding(decimal quantityChange, decimal costChange) =>
        AdjustmentWorld.Holding(InstrumentId, quantityChange, costChange);

    /// <summary>A <c>Buy</c> at 25.00 typed before orders, straight to the database.</summary>
    public Task<Guid> BuyAsync(decimal quantity) =>
        CustodialHttp.InsertManualTradeAsync(AccountId, "Buy", InstrumentId, quantity);

    public async Task<(decimal Quantity, decimal Cost)?> HoldingAsync()
    {
        var response = await AccountHttp.Send(HttpMethod.Get, $"/accounts/{AccountId}/holdings", TenantAdmin);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        var item = json.RootElement.GetProperty("items").EnumerateArray()
            .Where(row => row.GetProperty("instrumentId").GetGuid() == InstrumentId)
            .Select(row => (row.GetProperty("quantity").GetDecimal(), row.GetProperty("cost").GetDecimal()))
            .ToList();
        return item.Count == 0 ? null : item.Single();
    }

    public Task<(decimal Cash, decimal Reserved, decimal Available)> CashAsync() => Withdrawals.CashAsync();
}
