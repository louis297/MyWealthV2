using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Instruments;
using MyWealthV2.Application.FunctionalTests.Orders;
using MyWealthV2.Application.FunctionalTests.Transactions;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.InKindTransfers;

/// <summary>The <c>/in-kind-transfers</c> routes (docs/features/in-kind-transfers.md §8).</summary>
internal static class TransferHttp
{
    public const string Path = "/in-kind-transfers";
    public const string StaleRowVersion = "AAAAAAAAAAE=";

    public static Task<HttpResponseMessage> Send(HttpMethod method, string path, string? accessToken, object? body = null) =>
        AccountHttp.Send(method, path, accessToken, body);

    /// <summary>A transfer body; <c>In</c> carries the cost of 9 500.00 and an acquisition date.</summary>
    public static Dictionary<string, object?> Body(Guid accountId, Guid instrumentId, string direction = "In", decimal quantity = 100m)
    {
        var body = new Dictionary<string, object?>
        {
            ["accountId"] = accountId,
            ["direction"] = direction,
            ["instrumentId"] = instrumentId,
            ["quantity"] = quantity,
            ["changesBeneficialOwner"] = false,
            ["counterparty"] = new Dictionary<string, object?>
            {
                ["name"] = "Harbour Brokers",
                ["account"] = "HB-0044172",
                ["holderName"] = "J Smith"
            },
            ["memo"] = "Moving the Harbour holding across"
        };
        if (direction == "In")
        {
            body["cost"] = 9500.00m;
            body["acquiredOn"] = "2024-03-18";
        }

        return body;
    }

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

    /// <summary>Runs an action with the transfer's current <c>rowVersion</c>, read with <paramref name="readToken"/>.</summary>
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

    public static async Task<InKindTransfer> TransferAsync(Guid id)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.InKindTransfers.IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.PublicId == id);
    }

    /// <summary>The number of Transactions on the account.</summary>
    public static async Task<int> TransactionCountAsync(Guid accountPublicId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var account = await db.Accounts.IgnoreQueryFilters().SingleAsync(row => row.PublicId == accountPublicId);
        return await db.Transactions.IgnoreQueryFilters().CountAsync(t =>
            t.CashLegs.Any(leg => leg.AccountId == account.Id) || t.SecurityLegs.Any(leg => leg.AccountId == account.Id));
    }

    /// <summary>The holding item of the instrument on the account, or null.</summary>
    public static async Task<JsonElement?> HoldingAsync(string accessToken, Guid accountId, Guid instrumentId)
    {
        var response = await AccountHttp.Send(HttpMethod.Get, $"/accounts/{accountId}/holdings", accessToken);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        foreach (var item in json.RootElement.GetProperty("items").EnumerateArray())
        {
            if (item.GetProperty("instrumentId").GetGuid() == instrumentId)
            {
                return item.Clone();
            }
        }

        return null;
    }

    /// <summary>Disables an instrument through its route.</summary>
    public static async Task DisableInstrumentAsync(string accessToken, Guid instrumentId)
    {
        var read = await InstrumentHttp.Send(HttpMethod.Get, $"/instruments/{instrumentId}", accessToken);
        using var json = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        var disable = await InstrumentHttp.Send(HttpMethod.Post, $"/instruments/{instrumentId}/disable", accessToken,
            new { rowVersion = json.RootElement.GetProperty("rowVersion").GetString() });
        disable.StatusCode.ShouldBe(HttpStatusCode.NoContent, await disable.Content.ReadAsStringAsync());
    }
}

/// <summary>
/// The in-kind transfers test world: the orders world (spec §10) with an active USD safekeeping account on connection
/// <c>simulator</c>, and 100 ACME on Ned's Custodial USD account, bought by hand at 25.00 before orders.
/// </summary>
internal sealed record TransferWorld(OrderWorld Orders, SafekeepingAccount Safekeeping)
{
    public Tenant Tenant => Orders.Tenant;
    public User Customer => Orders.Customer;
    public Guid AccountId => Orders.AccountId;
    public Guid InstrumentId => Orders.InstrumentId;
    public string TenantAdmin => Orders.TenantAdmin;
    public string SecondTenantAdmin => Orders.People.SecondTenantAdmin.AccessToken;
    public string Adviser => Orders.Adviser;
    public string CustomerToken => Orders.CustomerToken;
    public string OtherCustomerToken => Orders.OtherCustomerToken;

    public static async Task<TransferWorld> CreateAsync(
        string name = "North Advisory",
        string code = "north-advisory",
        decimal holding = 100m,
        bool safekeeping = true)
    {
        var orders = await OrderWorld.CreateAsync(name, code);
        if (holding > 0)
        {
            await OrderHttp.InsertHoldingAsync(orders.AccountId, orders.InstrumentId, holding);
        }

        var account = safekeeping ? await SafekeepingHttp.EnsureActiveAsync(orders.Tenant.Id, "USD") : null;
        return new TransferWorld(orders, account!);
    }

    public Dictionary<string, object?> Body(string direction = "In", decimal quantity = 100m) =>
        TransferHttp.Body(AccountId, InstrumentId, direction, quantity);

    /// <summary>Requests a transfer as the Adviser.</summary>
    public Task<Guid> RequestAsync(string direction = "In", decimal quantity = 100m) =>
        TransferHttp.RequestAsync(Adviser, Body(direction, quantity));

    /// <summary>Requests as the Adviser and approves as Tess.</summary>
    public async Task<Guid> ApprovedAsync(string direction = "In", decimal quantity = 100m)
    {
        var id = await RequestAsync(direction, quantity);
        var response = await TransferHttp.ActAsync(TenantAdmin, id, "approve");
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        return id;
    }
}
