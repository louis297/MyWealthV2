using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Identity;

namespace MyWealthV2.Application.FunctionalTests.NominatedBankAccounts;

/// <summary>The <c>/nominated-bank-accounts</c> routes (docs/features/withdrawals.md §8).</summary>
internal static class NominatedBankAccountHttp
{
    public const string Path = "/nominated-bank-accounts";
    public const string StaleRowVersion = "AAAAAAAAAAE=";

    public static Task<HttpResponseMessage> Send(HttpMethod method, string path, string? accessToken, object? body = null) =>
        AccountHttp.Send(method, path, accessToken, body);

    public static Dictionary<string, object?> Body(
        Guid customerId,
        string currency = "USD",
        string accountNumber = "06-0123-0456789-00",
        string bankName = "ANZ",
        string accountName = "J Smith") => new()
    {
        ["customerId"] = customerId,
        ["currency"] = currency,
        ["bankName"] = bankName,
        ["accountName"] = accountName,
        ["accountNumber"] = accountNumber
    };

    public static Task<HttpResponseMessage> PostAsync(string accessToken, Dictionary<string, object?> body) =>
        Send(HttpMethod.Post, Path, accessToken, body);

    public static async Task<Guid> AddAsync(
        string accessToken,
        Guid customerId,
        string currency = "USD",
        string accountNumber = "06-0123-0456789-00")
    {
        var response = await PostAsync(accessToken, Body(customerId, currency, accountNumber));
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, content);
        using var json = JsonDocument.Parse(content);
        var id = json.RootElement.GetProperty("id").GetGuid();
        response.Headers.Location!.ToString().ShouldEndWith($"{Path}/{id}");
        return id;
    }

    public static async Task<JsonElement> GetAsync(string accessToken, Guid id)
    {
        var response = await Send(HttpMethod.Get, $"{Path}/{id}", accessToken);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.Clone();
    }

    public static async Task<JsonElement> ListAsync(string accessToken, string query = "")
    {
        var response = await Send(HttpMethod.Get, Path + query, accessToken);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.Clone();
    }

    public static async Task<List<Guid>> ListIdsAsync(string accessToken, string query = "") =>
        (await ListAsync(accessToken, query)).GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .ToList();

    /// <summary>Posts an action with the row's current <c>rowVersion</c> (read with <paramref name="readToken"/>, the
    /// acting token when null) and <paramref name="note"/>.</summary>
    public static async Task<HttpResponseMessage> ActAsync(
        string accessToken,
        Guid id,
        string action,
        string? note = "Checked",
        string? readToken = null)
    {
        var rowVersion = (await GetAsync(readToken ?? accessToken, id)).GetProperty("rowVersion").GetString();
        return await Send(HttpMethod.Post, $"{Path}/{id}/{action}", accessToken, new { note, rowVersion });
    }

    public static async Task VerifyAsync(string accessToken, Guid id, string note = "Bank statement sighted")
    {
        var response = await ActAsync(accessToken, id, "verify", note);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
    }

    /// <summary>Every route, with a body each action accepts.</summary>
    public static IEnumerable<(HttpMethod Method, string Route, object? Body)> Routes(Guid id, Guid customerId)
    {
        yield return (HttpMethod.Get, Path, null);
        yield return (HttpMethod.Get, $"{Path}/{id}", null);
        yield return (HttpMethod.Post, Path, Body(customerId));
        yield return (HttpMethod.Post, $"{Path}/{id}/verify", new { note = "Note", rowVersion = StaleRowVersion });
        yield return (HttpMethod.Post, $"{Path}/{id}/reject", new { note = "Note", rowVersion = StaleRowVersion });
        yield return (HttpMethod.Post, $"{Path}/{id}/remove", new { note = "Note", rowVersion = StaleRowVersion });
    }
}

/// <summary>
/// One tenant with two TenantAdmins (Tess signs in, Tia is the second), an Adviser (Ann) with a Customer (Ned), and
/// a second Adviser (Bob) with a Customer (Olga) that Ann cannot see (withdrawals §10).
/// </summary>
internal sealed record NominatedWorld(
    Tenant Tenant,
    User Adviser,
    User Customer,
    User OtherCustomer,
    TokenResponse TenantAdmin,
    TokenResponse SecondTenantAdmin,
    TokenResponse AdviserToken,
    TokenResponse CustomerToken,
    TokenResponse OtherCustomerToken)
{
    public static async Task<NominatedWorld> CreateAsync(string name = "North Advisory", string code = "north-advisory")
    {
        var (tenant, adviser, customer, tokens) = await AccountHttp.SignInTenantAdmin(
            name, code, $"tess@{code}.example", adviserEmail: $"ann@{code}.example", customerEmail: $"ned@{code}.example");
        var tia = await TestApp.CreatePersonAsync(UserRole.TenantAdmin, "Tia Admin", $"tia@{code}.example", "Password1!", tenant.Id);
        var bob = await TestApp.CreatePersonAsync(UserRole.Adviser, "Bob Adviser", $"bob@{code}.example", "Password1!", tenant.Id);
        var olga = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Olga Other", $"olga@{code}.example", "Password1!", tenant.Id, bob.Id);
        return new NominatedWorld(
            tenant, adviser, customer, olga, tokens,
            await TestApp.IssueAccessTokenAsync(tia),
            await TestApp.IssueAccessTokenAsync(adviser),
            await TestApp.IssueAccessTokenAsync(customer),
            await TestApp.IssueAccessTokenAsync(olga));
    }
}
